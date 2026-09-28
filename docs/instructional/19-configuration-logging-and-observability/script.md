# 19 · Configuration, Logging and Observability

Welcome. Configuration and logging look like plumbing, so in most migrations they get moved last and carelessly. In FeeBilling they hide real risk: per-client behaviour in XML transforms, production passwords in source control, a feature flag set in a file the process that uses it never reads, and new services that collect telemetry and then throw it away. By the end of this lesson, you should be able to explain what replaces `Web.config` and config transforms, migrate configuration safely, argue for structured logging, and trace a single request across the old and new systems.

## The questions this lesson answers

Here's what you'll be ready for. What replaces `Web.config` and config transforms? How do you migrate configuration safely? Structured logging versus string logging? How do you trace a request across old and new systems? And a practical one: what would you put on the billing dashboard?

Interviewers ask these to find out whether you treat operability as part of the migration, or as something to sort out after go-live. In a billing system, the answer has to be the former. If you can't see a billing run, you can't prove it's correct.

## Inventory before you migrate

Start with the evidence. Open `legacy/FeeBilling.Web/Web.config` and look at the app settings section. Apart from the standard MVC keys, there are ten custom keys. Now search the legacy code for every read of `ConfigurationManager.AppSettings`. You'll find exactly three reads in the whole legacy folder. In the web application, two of the ten keys are read: `Invoice.LogoPath`, by the PDF renderer, and `RemoteAppApiKey`, by `Global.asax.cs` for the remote authentication server. The third read is in the billing runner, and we'll come back to it.

So most of this configuration is read by nothing. Let's classify it, because classification is the method you'd use on any real system.

`Billing.DefaultFirmId` looks important, but nothing reads it. The home page view, `legacy/FeeBilling.Web/Views/Home/Index.cshtml`, hard-codes a default firm ID of one in JavaScript. Delete the key, and later derive the firm from the user's claims.

`Billing.DayCountBasis` has a comment that says it's documentation only: the fee calculators always divide by four, and nothing reads this key. That's more than dead config. It's a contract term. Whether a firm is billed on annual divided by four, or on actual days over 365, is part of the fee agreement. It doesn't belong in a file that differs by server. It belongs in the database, as part of a per-firm billing policy, versioned and audited.

`Feature.FlowProRating` is read by nothing, because flow pro-rating is still a TODO in the code. Delete it until the feature exists. `Invoice.FooterText` is read by nothing. The two SMTP keys and the custodian feed archive path aren't read by anything in this repository. They might be read by tools that live elsewhere, so check the deployment scripts before deleting them. And the archive path is a UNC path to a file server, which becomes a blob container name in the new design.

Now the most interesting finding. Open `legacy/FeeBilling.Web/Web.ClientX.config`. It's a transform applied at publish time for ClientX, an enterprise client with a dedicated instance. It sets the day-count basis to actual over 365, with the comment that nothing reads it. And it sets `Feature.HouseholdBilling` to false.

But the web application never reads that flag. The only code that reads it is `BillingRunnerService.cs`, which checks whether the setting equals the string true. And the billing runner reads its own configuration file, `legacy/FeeBilling.BillingRunner/App.config`, which says true. So unless someone edits ClientX's runner configuration separately, which you'd check in the deployment scripts, ClientX's households are being billed despite the setting that says household billing is off. That's what happens when a flag lives in more than one process's configuration.

The lesson for an interview: inventory first. Grep every key against the code, delete dead keys instead of migrating them, move business rules into data, and rotate every secret that was ever committed.

## From transforms to layered configuration

What replaces config transforms? The principle is build once, configure per environment. Legacy builds a different artifact for each client by applying XML transforms at publish time. Modern .NET builds one artifact and layers configuration at runtime.

`WebApplication.CreateBuilder` already loads providers in order: `appsettings.json`, then `appsettings` dot the environment name dot JSON, then user secrets in development, then environment variables, then the command line. Later providers win. So shared defaults go in `appsettings.json`, per-environment values in the environment file or environment variables, and you never rebuild for a client.

For per-client settings, the natural replacement for `Web.ClientX.config` is Azure App Configuration with labels. You load the shared values, which have no label, and then the values labelled with the client's name, so the client's values override the shared ones. Things like the invoice logo and the SMTP sender address go there, per client.

Feature flags that change invoices, like household billing, need more than a boolean. They need to be evaluated per firm, and they need an audit trail of who changed what and when. App Configuration feature flags or the `Microsoft.FeatureManagement` library give you that.

Watch the provider order. Any provider you add after `CreateBuilder` comes after the environment variables, so it overrides them. Decide deliberately whether an operator's environment variable should be able to override central configuration, and order the providers to match.

## Secrets

Now the uncomfortable part. `legacy/FeeBilling.Web/Web.Release.config` and `Web.ClientX.config` contain production SQL logins with their passwords. There are two machine keys committed, one in the base `Web.config` and one for ClientX. And the remote-app API key appears in `Web.config`, and the same value appears in `src/FeeBilling.Accounts.Api/appsettings.json`. So the new code repeated the old mistake.

What do you do? Rotate every one of them. Deleting a secret from a file doesn't un-leak it, because it's still in the git history and in every clone. Rewriting history is optional. Rotation isn't. Then move secrets to Azure Key Vault, reached with managed identity, so there's no credential to store at all. App Configuration can hold Key Vault references, so the application sees one configuration tree. Locally, developers use `dotnet user-secrets`. And add secret scanning to CI, so it can't happen again. That's in lesson twenty-four.

## Options and validation

Once configuration is layered, bind it to strongly typed options. The pattern is `AddOptions` of your options class, then `BindConfiguration` with the section name, then `ValidateDataAnnotations`, then `ValidateOnStart`. Validation attributes like required and string length describe what's valid, and there's an options validation source generator, the `OptionsValidator` attribute, that generates the validator at compile time instead of using reflection.

`ValidateOnStart` is the important part. Think about the legacy invoice logo. The PDF renderer reads the logo path lazily, the first time an invoice is rendered. A bad path in a legacy deployment isn't discovered until someone asks for a PDF. With validation at startup, a missing or invalid setting stops the application from starting, which a deployment pipeline notices immediately. A bad value should stop startup, not break the forty-thousandth invoice PDF.

Know the three interfaces. `IOptions` of T reads the value once and is a singleton. `IOptionsSnapshot` of T is scoped, recomputed per request, so it picks up reloaded values. `IOptionsMonitor` of T is a singleton that always returns the current value and can notify you of changes. The trap: injecting a snapshot into a singleton fails scope validation. For reloadable settings in singletons, use the monitor.

## Structured logging

Now logging. Open `legacy/FeeBilling.Core/FeeCalculator.cs` and look at the last lines: log dot Info, with the string "Fee for", plus the account ID, plus "equals", plus the fee. Every calculation writes one INFO line built by string concatenation. On a billing run for a large firm, that's tens of thousands of lines of text.

What's wrong with that? Two things. First, the result is unsearchable text. You can't ask the log system for every fee over a thousand dollars, or every line for account 1001, without parsing strings. Second, the concatenation allocates even when INFO logging is turned off.

The modern replacement is `ILogger` of T with message templates. You write the message as "Fee calculated for account", then AccountId in braces, then Fee in braces, and pass the values as arguments. The log provider keeps AccountId and Fee as named properties you can query and aggregate. Even better, use the `LoggerMessage` attribute on a partial method. The source generator writes the logging code at compile time, with the level check, no boxing and a stable event ID. Configure levels by category, and demote the per-account line to DEBUG, replacing it with a metric.

Also look at where legacy logs go. The log4net section of `Web.config` configures a rolling file appender that writes to a folder under the C drive on each server, and the runner and the ingestion service have their own files. To investigate one billing run, someone logs on to several machines and searches text files. In containers, local files disappear with the container. So the new services write to the console and to an OpenTelemetry exporter, and a central store collects everything.

Structured logging creates a new risk, and saying so is what makes your answer senior. Structured logs are indexed. Everything in them becomes searchable. Look at `legacy/FeeBilling.Web/Controllers/Mvc/AccountController.cs`: on a failed login, it logs the user name and the client's IP address. And account IDs appear everywhere. In a log index, that's a queryable client list. So classify personal data and redact it. .NET has a compliance and redaction library for this: you mark parameters with a data classification attribute, and a redactor erases them, or replaces them with a keyed hash so support can correlate lines without seeing the value. Check the current API names before you rely on them. Legacy even has the logic already: `AccountNumberFormatter.Mask` in `legacy/FeeBilling.Core/AccountNumberFormatter.cs` masks all but the last four characters for statements. It was just never applied to logs.

## OpenTelemetry end to end

Finally, observability. Open `src/FeeBilling.ServiceDefaults/ServiceDefaultsExtensions.cs`. It configures OpenTelemetry for every new service: logging, metrics for ASP.NET Core, the HTTP client and the runtime, and tracing, with health endpoints filtered out. And then a comment: TODO, no exporter yet. Telemetry is collected and then dropped. OTLP to a collector versus Azure Monitor was never decided.

So the first step is to decide, and write the ADR. OTLP, the OpenTelemetry protocol, sent to a collector, keeps you vendor-neutral. Azure Monitor is the natural choice if you're all-in on Azure. Either is fine. Having no decision isn't. Once it's decided, call `UseOtlpExporter` when the endpoint is configured, or the Azure Monitor equivalent.

Then add billing-specific telemetry. Create a `Meter` named after the billing domain, through `IMeterFactory`, with a counter for accounts billed and a histogram for run duration. Tag them with the firm ID, never the account ID. A firm ID gives you forty time series. An account ID gives you three hundred and fifty thousand, which is called high cardinality, and it will hurt your metrics backend and your bill. Add an `ActivitySource` for the billing run and its chunks, so a run shows up as one trace.

Now tracing across old and new, which is the interview question. The standard is W3C trace context: a header called traceparent that carries the trace ID from service to service. Walk the hops. Browser to gateway to API: automatic, because ASP.NET Core and HTTP client instrumentation propagate it, and the gateway forwards it. Verify that in a trace viewer. API to outbox to Service Bus: not automatic. The dispatcher publishes later, on its own activity, so the trace would break. Store the current activity's ID in the outbox row, and set it on the message. Service Bus to worker: start the consumer's activity with the stored traceparent as its parent, or as a link for batch work. And gateway to legacy IIS: legacy has no OpenTelemetry. So make a small additive change in `Global.asax.cs`: at the beginning of each request, read the traceparent header and put the trace ID into a log4net context property, printed in the log pattern. Now one trace ID finds every line, in the new services and in legacy.

For a local demo, you can run the standalone Aspire dashboard in a container as an OTLP endpoint, point the gateway and Accounts.Api at it, and watch one trace span both.

And the billing dashboard? Run duration, accounts per second, failed and quarantined counts, dead-letter queue depth, parity differences, and error rate by firm. Page on three things: throughput dropping during a run, messages in the dead-letter queue, and any unexplained parity difference.

## Traps

A few traps to call out.

Migrating every key. Half of these are read by nothing.

Business rules as deployment config. A firm's day-count basis is a contract term, so it belongs in audited data.

Flags that live in more than one process's configuration, like the ClientX household flag.

Committed secrets. Deleting doesn't un-leak. Rotation does.

Provider order surprises, where a central store overrides an operator's environment variable, or the other way round.

Injecting a snapshot into a singleton. Use the monitor.

Making PII searchable with structured logs.

High-cardinality metric tags.

And assuming trace context crosses the outbox. It doesn't unless you carry it.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** What replaces Web.config and config transforms?

[pause 5s]

Layered configuration: `appsettings.json`, an environment-specific file, environment variables, and a central store like Azure App Configuration with a label per client instead of a transform per client. Secrets live in Key Vault, reached with managed identity, and user secrets locally. Everything binds to strongly typed options validated at startup. You build once and configure per environment, and you never rebuild per client.

**Interviewer:** How do you migrate configuration safely?

[pause 5s]

Inventory first: grep every key against the code. In FeeBilling, the web app reads two of its ten custom keys. Delete dead keys rather than migrating them. Move business rules, like a firm's day-count basis, into versioned data. Check for flags that are read by a different process than the one they're set for. And rotate every secret that was ever committed.

**Interviewer:** Structured logging or string logging?

[pause 5s]

Structured. Message templates keep named properties like account ID and fee, so you can query and aggregate them, while concatenation produces text you can't search and allocates even when the level is off. The `LoggerMessage` source generator removes the remaining overhead. But structured logs index everything, so I'd classify personal data and redact it, like the user names and IP addresses legacy logs on failed logins.

**Interviewer:** How do you trace a request across old and new systems?

[pause 5s]

W3C trace context. ASP.NET Core and HTTP client instrumentation propagate the traceparent header automatically, and the gateway forwards it. Messages don't carry it automatically, especially through an outbox where publishing happens later, so I store the trace context in the outbox row and restore it in the consumer. Legacy has no OpenTelemetry, so I'd read the header in `Global.asax` and put the trace ID into the log4net context, so one ID finds everything.

**Interviewer:** What would you put on the billing dashboard?

[pause 5s]

Run duration, accounts per second, failed and quarantined counts, dead-letter depth, parity differences, and error rate by firm, tagged by firm, never by account. I'd alert on throughput dropping mid-run, anything in the dead-letter queue, and any unexplained parity difference.

## Recap

Five things to remember from this lesson.

One: inventory configuration against the code before migrating it. In FeeBilling, the web app reads two of ten keys, the day-count basis is a contract term disguised as config, and ClientX's household flag is set in a file the runner never reads.

Two: replace transforms with layered providers and App Configuration labels per client. Build once, configure per environment.

Three: rotate every committed secret, move secrets to Key Vault with managed identity, and validate options at startup.

Four: use message templates and source-generated logging, and treat structured logs as a searchable database that needs PII redaction.

Five: choose an exporter, add billing metrics tagged by firm, and carry trace context through the outbox and into legacy, so one trace ID finds everything.

In the next lesson, we'll replace Forms authentication with OpenID Connect, move to policy-based authorization, and enforce tenant isolation between firms.
