# 19 · Configuration, Logging and Observability

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-07 · **Prerequisites:** 08, 09

**Audio lesson:** [19-configuration-logging-and-observability.mp3](19-configuration-logging-and-observability.mp3) · [Transcript](script.md)

## Why this video exists

Configuration and logging look like plumbing, so they get migrated last and carelessly. In FeeBilling they hide real risk: per-client behaviour lives in XML transforms applied at publish time, production passwords sit in source control, one feature flag is set in a file the process that uses it never reads, and log4net writes one string-concatenated INFO line per account per billing run. The new services collect OpenTelemetry data and then drop it, because nobody chose an exporter. Interviewers ask "what replaces `Web.config` and transforms?" and "how would you trace a request across the old and new systems?" to see whether you treat operability as part of the migration. This video inventories the legacy configuration, maps each key to where it belongs, and makes a single billing request traceable from the gateway to the worker and into legacy.

## Learning objectives

By the end, the viewer can:

- Inventory `appSettings` against the code that reads them, and classify each key: dead, deployment config, per-client config, feature flag, secret, or business data.
- Replace config transforms with layered providers: `appsettings.{Environment}.json`, environment variables, Azure App Configuration labels per client, and Key Vault for secrets.
- Bind strongly typed options with validation that fails at startup, and choose between `IOptions`, `IOptionsSnapshot` and `IOptionsMonitor`.
- Replace string-concatenated log4net calls with `ILogger<T>` message templates and source-generated `[LoggerMessage]` methods, with PII kept out of searchable logs.
- Finish the OpenTelemetry setup: choose an exporter, add custom billing metrics and activities, and propagate trace context across the gateway, APIs, Service Bus, the worker and legacy IIS.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| What replaces `Web.config` and config transforms? | Layered configuration: `appsettings.json`, `appsettings.{Environment}.json`, environment variables, and a central store (Azure App Configuration, with labels per client or environment). Secrets in Key Vault, reached with managed identity. Strongly typed options validated at startup. Build once, configure per environment, never rebuild per client. |
| How do you migrate configuration safely? | Inventory first: grep every key against the code. Delete dead keys rather than migrating them. Move business rules (per-firm contract terms) into data. Rotate every secret that was ever committed. |
| Structured logging vs string logging? | Message templates keep named properties (`{AccountId}`) that you can query and aggregate. Concatenation produces unsearchable text and allocates even when the level is disabled. `[LoggerMessage]` source generation removes boxing and level-check overhead. Structured logs index everything, so PII needs classification and redaction. |
| How do you trace a request across old and new systems? | W3C trace context (`traceparent`). ASP.NET Core and `HttpClient` instrumentation propagate it automatically; the gateway forwards it; messages carry it; the outbox must store it because publishing happens later. Legacy reads the header and puts the trace ID into its log context, so one ID finds everything. |
| What would you put on the billing dashboard? | Run duration, accounts per second, failed and quarantined counts, dead-letter depth, parity differences, and error rate by firm. Alerts on throughput dropping and on any unexplained parity difference. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Web/Web.config` | `appSettings`: `Billing.DayCountBasis` ("Documentation only ... Nothing reads this key"), `Feature.*`, UNC path, SMTP, `RemoteAppApiKey`, `machineKey`, connection strings |
| `legacy/FeeBilling.Web/Web.Release.config`, `legacy/FeeBilling.Web/Web.ClientX.config` | Transforms applied at publish; production SQL logins and a second `machineKey` committed. **Don't show the values on screen** |
| `legacy/FeeBilling.BillingRunner/App.config`, `BillingRunnerService.cs` | `Feature.HouseholdBilling` read with `ConfigurationManager` from the runner's *own* config |
| `legacy/FeeBilling.Web/Views/Home/Index.cshtml` | `window.feeBillingConfig = { defaultFirmId: 1 };`, hard-coded, so `Billing.DefaultFirmId` is dead too |
| `legacy/FeeBilling.Core/FeeCalculator.cs`, `legacy/FeeBilling.Web/Controllers/Mvc/AccountController.cs` | `Log.Info("Fee for " + accountId + " = " + fee);` and `Log.Warn("Failed login for " + model.UserName + " from " + Request.UserHostAddress);` |
| `legacy/FeeBilling.Core/AccountNumberFormatter.cs` | `Mask()` already exists: redaction logic legacy never applied to logs |
| `src/FeeBilling.ServiceDefaults/ServiceDefaultsExtensions.cs` | OpenTelemetry configured; "TODO: no exporter yet. Telemetry is collected and then dropped." |
| `src/FeeBilling.Accounts.Api/appsettings.json` | `RemoteApp:ApiKey` committed, the same value as legacy's `RemoteAppApiKey` |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | `Web.config` has ten custom `appSettings` keys. The web app reads two of them. Run the grep and show it. |
| 01:30–05:00 | Configuration inventory | Walk the inventory table below. Three findings: dead keys; a per-firm *contract term* (`Billing.DayCountBasis`) stored as deployment config; and `Feature.HouseholdBilling=false` for ClientX set in a transform of `Web.config`, while the only code that reads the flag is the BillingRunner, whose own `App.config` says `true`. Unless ClientX's runner config is edited separately (check the deployment scripts), ClientX's households are billed despite the "off" setting. |
| 05:00–08:00 | Transforms to layered providers | Build once, configure per environment. Provider order: later wins. App Configuration labels replace `Web.ClientX.config`. What goes where (table below). Feature flags that change invoices need an audit trail of who changed what and when. |
| 08:00–10:00 | Secrets | Production SQL passwords in `Web.Release.config` and `Web.ClientX.config`, two `machineKey`s, the remote-app API key in both stacks. Rotate them all; history rewriting is optional, rotation isn't. Key Vault with managed identity in Azure; `dotnet user-secrets` locally. |
| 10:00–12:30 | Options and validation | `AddOptions<T>().BindConfiguration().ValidateDataAnnotations().ValidateOnStart()`, the `[OptionsValidator]` source generator, and the lifetimes of `IOptions`, `IOptionsSnapshot` and `IOptionsMonitor`. A missing logo path should stop the app starting, not break the 40,000th invoice PDF. |
| 12:30–15:30 | Logging | log4net to `ILogger<T>`: templates, `[LoggerMessage]`, event IDs, levels by category. One INFO line per account per run becomes a DEBUG line plus a metric. PII: user names and IP addresses in login logs, account numbers everywhere; classification and redaction. |
| 15:30–18:30 | OpenTelemetry end to end | Pick the exporter (OTLP to a collector, or Azure Monitor) and remove the TODO. Custom `Meter` and `ActivitySource` for billing. Trace context: gateway → API (automatic), API → outbox → Service Bus → worker (store the `traceparent` in the outbox row), gateway → legacy IIS (read the header into log4net). Live: traces in the standalone Aspire dashboard. |
| 18:30–20:00 | Recap | The dashboard you'd build for a billing run, and the three alerts you'd page on. |

### Inventory of the legacy `appSettings`

| Key | Read by (in this repository) | Where it belongs |
|---|---|---|
| `Billing.DefaultFirmId` | Nothing (`Index.cshtml` hard-codes `defaultFirmId: 1`) | Delete; derive from the user's firm claim (video 20) |
| `Billing.DayCountBasis` | Nothing ("Documentation only") | **Data**: per-firm `BillingPolicy` in the database (video 05) |
| `Feature.HouseholdBilling` | BillingRunner, from its own `App.config` | A feature flag evaluated per firm, with an audit trail |
| `Feature.FlowProRating` | Nothing (FB-212 is still a TODO) | Delete until the feature exists |
| `Invoice.LogoPath` | `InvoicePdfRenderer` | Per-client setting (App Configuration label), pointing at a blob, not `D:\ClientX\...` |
| `Invoice.FooterText` | Nothing | Per-client setting, or delete |
| `Smtp.Host`, `Smtp.From` | Nothing in this repository (check other tools and deploy scripts) | Options, with `Smtp.From` per client |
| `Custodian.FeedArchivePath` | Nothing in this repository (the archive job lives elsewhere) | Blob container name (video 17) |
| `RemoteAppApiKey` | `Global.asax.cs` | Secret: Key Vault |

### Legacy: the flag in the wrong file

```xml
<!-- legacy/FeeBilling.Web/Web.ClientX.config -->
<!-- ClientX's fee agreement says actual/365. See Billing.DayCountBasis in Web.config: nothing reads it. -->
<add key="Billing.DayCountBasis" value="Actual/365" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
<add key="Feature.HouseholdBilling" value="false" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
```

```csharp
// legacy/FeeBilling.BillingRunner/BillingRunnerService.cs: reads the runner's App.config, which says "true"
if (ConfigurationManager.AppSettings["Feature.HouseholdBilling"] == "true")
```

### After: layered configuration and validated options

```csharp
var builder = WebApplication.CreateBuilder(args);
// Already loaded, in order: appsettings.json, appsettings.{Environment}.json, user secrets (Development),
// environment variables, command line. Providers added below come *after* them and win.

if (builder.Configuration["AppConfig:Endpoint"] is { } endpoint)
{
    var client = builder.Configuration["FeeBilling:Client"] ?? "shared";   // "ClientX" on the dedicated instance
    var credential = new DefaultAzureCredential();

    builder.Configuration.AddAzureAppConfiguration(options => options
        .Connect(new Uri(endpoint), credential)
        .Select(KeyFilter.Any, LabelFilter.Null)   // shared values
        .Select(KeyFilter.Any, client)             // per-client values override (replaces Web.ClientX.config)
        .ConfigureKeyVault(kv => kv.SetCredential(credential)));   // secrets resolved from Key Vault references
}

builder.Services.AddOptions<InvoiceOptions>()
    .BindConfiguration(InvoiceOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();                            // a bad value stops startup, not invoice 40,000

public sealed class InvoiceOptions
{
    public const string Section = "Invoice";

    [Required] public string LogoBlobName { get; set; } = "";
    [Required, StringLength(500)] public string FooterText { get; set; } = "";
}
```

| Kind of setting | Where it lives | Legacy example |
|---|---|---|
| Same everywhere | `appsettings.json` | Log levels |
| Per environment | `appsettings.{Environment}.json`, environment variables | Service URLs |
| Per client | App Configuration, label per client | `Invoice.LogoPath`, `Smtp.From` |
| Feature flag | App Configuration feature flags or `Microsoft.FeatureManagement`, with audit | `Feature.HouseholdBilling` |
| Secret | Key Vault (managed identity); user secrets locally | Connection strings, `machineKey`, `RemoteAppApiKey` |
| Business rule | Database, versioned and audited | `Billing.DayCountBasis` |

Verify the App Configuration and Key Vault provider APIs against current package docs before recording.

### After: structured, source-generated logging

```csharp
internal static partial class BillingLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug,
        Message = "Fee calculated for account {AccountId}: {Fee} using {ScheduleCode}")]
    public static partial void FeeCalculated(this ILogger logger, int accountId, decimal fee, string scheduleCode);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Failed login for {UserName}")]
    public static partial void LoginFailed(this ILogger logger, [ClientData] string userName);   // classified: redacted
}

// Program.cs: classification-aware redaction (Microsoft.Extensions.Compliance.Redaction + Telemetry packages;
// verify the API names before recording)
builder.Logging.EnableRedaction();
builder.Services.AddRedaction(r =>
    r.SetRedactor<ErasingRedactor>(new DataClassificationSet(FeeBillingTaxonomy.ClientData)));
```

`[ClientData]` is a custom attribute deriving from `DataClassificationAttribute`. An HMAC-based redactor is the alternative when support needs to correlate the same user across log lines without seeing who it is.

### After: an exporter, billing metrics and trace context

```csharp
// ServiceDefaultsExtensions.ConfigureOpenTelemetry: replaces the TODO
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(BillingMetrics.MeterName));

if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry().UseOtlpExporter();   // or .UseAzureMonitor(): decide, write the ADR
}

public sealed class BillingMetrics
{
    public const string MeterName = "FeeBilling.Billing";
    private readonly Counter<long> _accountsBilled;
    private readonly Histogram<double> _runDuration;

    public BillingMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        _accountsBilled = meter.CreateCounter<long>("feebilling.billing.accounts_billed", unit: "{account}");
        _runDuration = meter.CreateHistogram<double>("feebilling.billing.run.duration", unit: "s");
    }

    public void ChunkCompleted(int accounts, int firmId) =>
        _accountsBilled.Add(accounts, new KeyValuePair<string, object?>("feebilling.firm_id", firmId));   // never an account ID as a tag

    public void RunCompleted(TimeSpan elapsed, int firmId) =>
        _runDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("feebilling.firm_id", firmId));
}
```

| Hop | How trace context crosses it |
|---|---|
| Browser → Gateway → Accounts.Api / Billing.Api | ASP.NET Core and `HttpClient` instrumentation (already in ServiceDefaults); YARP forwards `traceparent` (verify with the dashboard) |
| Billing.Api → outbox → Service Bus | **Not automatic.** The dispatcher publishes later, on its own activity. Store `Activity.Current?.Id` in the outbox row and set it on the message |
| Service Bus → Billing.Worker | Start the consumer's activity with the stored `traceparent` as parent (or as a link, for batch work) |
| Gateway → legacy IIS | Legacy has no OpenTelemetry. An additive change in `Application_BeginRequest` reads `traceparent` and puts the 32-character trace ID into a log4net context property, printed with `%property{TraceId}` |

The ServiceDefaults tracing setup already calls `.AddSource(builder.Environment.ApplicationName)`, so an `ActivitySource` named after the service's application name is exported without further registration.

## Demo

```bash
# Which appSettings keys are actually read, and by what?
git grep -n "<add key=" -- legacy/FeeBilling.Web/Web.config
git grep -nE "AppSettings\[|ConnectionStrings\[" -- legacy

# Secrets inventory: list the files, don't print the values on the recording
git grep -lE "password=|Password=|machineKey|ApiKey" -- legacy src

# Local secrets for the new services
dotnet user-secrets init --project src/FeeBilling.Accounts.Api
dotnet user-secrets set "RemoteApp:ApiKey" "<rotated value>" --project src/FeeBilling.Accounts.Api

# See the telemetry that's currently being dropped: standalone Aspire dashboard as an OTLP endpoint
# (check the image's docs for current ports; the UI prints a login token in its logs)
docker run --rm -it -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 dotnet run --project src/FeeBilling.Accounts.Api
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 dotnet run --project src/FeeBilling.Gateway
curl -i "http://localhost:5000/api/accounts?firmId=1"   # one trace spanning gateway and API

# Custom metrics without any exporter
dotnet-counters monitor --name FeeBilling.Billing.Worker --counters FeeBilling.Billing
```

The dashboard only receives data once the exporter change above is in place. Recording the "before" state (nothing arrives) is part of the point.

## Traps to call out

- **Migrating every key.** Half of these keys are read by nothing. Migrating dead config gives it a second life and a false sense of meaning.
- **Business rules as deployment config.** A firm's day-count basis is a contract term. It belongs in versioned, audited data, not in a file that differs by server.
- **Flags that live in more than one process's config.** The ClientX household flag shows what happens: the value is set in the process that doesn't read it.
- **Committed secrets.** Deleting them from the file doesn't un-leak them; rotation does. Put secret scanning in CI (video 24).
- **Provider order surprises.** A provider added after `CreateBuilder` overrides environment variables. Decide whether an operator's environment variable should be able to override App Configuration, and order the providers accordingly.
- **Snapshots in singletons.** `IOptionsSnapshot<T>` is scoped; injecting it into a singleton fails scope validation. Use `IOptionsMonitor<T>` for reloadable settings in singletons.
- **Structured logs make PII searchable.** Legacy logs user names and IP addresses on failed logins and account IDs everywhere. In a log index, that's a queryable client list.
- **High-cardinality metric tags.** A firm ID is fine as a tag; an account ID creates 350,000 time series.
- **Assuming trace context crosses the outbox.** It doesn't unless you carry it. Without it, the worker's traces start from nowhere.

## Key terms

Configuration provider · config transform · Azure App Configuration label · Key Vault reference · managed identity · options pattern · `ValidateOnStart` · `IOptionsMonitor` · feature flag · message template · `[LoggerMessage]` · data classification · redaction · OpenTelemetry · OTLP · `Meter` · `ActivitySource` · W3C trace context · metric cardinality

## After the video

1. Complete the configuration inventory for `Web.config`, the transforms and the runner's `App.config`, and file a ticket for the ClientX household-billing flag.
2. Write the exporter ADR (OTLP collector vs Azure Monitor) and remove the TODO from ServiceDefaults.
3. Define the billing run dashboard: five metrics, three alerts, and the log query that finds every line for one trace ID across new services and legacy.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 6.2 (Config, Logging), Section 9 WP-07, Section 13 (config and logging mappings)
- Microsoft Learn: *Configuration in ASP.NET Core*, *Options pattern*, *Options validation source generator*, *Compile-time logging source generation*, *Data redaction in .NET*, *.NET observability with OpenTelemetry*, *Azure App Configuration .NET provider*, *Standalone Aspire dashboard*
- W3C *Trace Context* specification
