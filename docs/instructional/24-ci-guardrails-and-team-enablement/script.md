# 24 · CI Guardrails and Team Enablement

Welcome to the final lesson. The job title says subject-matter expert, and that means other people learn from you. The FeeBilling migration will be finished by a team, not by one person, and most of that team won't have heard the previous twenty-three lessons. Your job is to make the right way the easy way, and to make the wrong way fail CI before it ever reaches a code review. By the end of this lesson, you should be able to turn the traps from this series into automated guardrails, and turn your knowledge into assets the whole team uses. It's also your answer to the interview question aimed squarely at the "SME" in the job title.

## The questions this lesson answers

Here are the questions this lesson prepares you for. How would you make other developers productive on the migration? How do you stop people from reintroducing legacy patterns? What goes in CI for a migration? And how do you measure migration progress?

Here's the principle that ties them together. Every trap in this series should end up in one of three places. Either it's impossible to write, because the template or the type system prevents it. Or it fails CI, because an analyzer or a test catches it. Or it shows up in a report, so its trend is visible. A trap that only lives in someone's head, or in a review comment, will be reintroduced.

## CI today, and a trusted legacy subset

Open `.github/workflows/ci.yml`. There are two jobs on two operating systems, and that part is right. The legacy job builds the .NET Framework solution on `windows-latest`. The modern job builds the .NET 10 solution on `ubuntu-latest`, and its tests start a real SQL Server using Testcontainers, because Docker is available on the Linux runner.

Now look at the legacy test step. It has `continue-on-error: true`, and a comment: known failures, nine, and ignored tests, twenty-two; nobody trusts this suite; it doesn't gate merges. A test job that can't fail is decoration.

The fix isn't to delete the suite, and it isn't to fix all thirty-one problem tests before anything else. It's to quarantine them explicitly. Give each failing or ignored test a test category of Quarantine, with a ticket. Open `legacy/FeeBilling.Tests/Core/FeeCalculatorTests.cs` and you'll see why they're untrusted: tests ignored because they need a database, and one ignored with the reason "Flaky - cache from previous test". Then change the CI step to run everything except the quarantine category, without continue-on-error. Now you have a small suite that's trusted and gating, instead of a large one that's ignored. The quarantine list only ever shrinks, and the golden master from lesson four is how most of those tests will eventually be superseded.

Next, look at the build settings. The repository root has `Directory.Build.props`, which applies to `src` and `tests`: nullable reference types on, `TreatWarningsAsErrors` true, and `AnalysisLevel` set to latest. That's a good foundation. But `legacy/Directory.Build.props` deliberately doesn't inherit it. It sets `AnalysisLevel` to none, and its `NoWarn` list includes `CS0618`, the warning for using obsolete APIs. So every obsolete-API warning in legacy is invisible. That's understandable for code left as found. The trap is letting a copy of that line reach `src`.

Read the modern job critically too, and list what's missing. It builds and tests, and that's all. There's no parity check, because the parity project doesn't exist yet. There's no analyzer that stops legacy patterns creeping into new code. There's no architecture test keeping the domain pure. There's no dependency or licence check, no container image being produced, and no report of how far the migration has come. Each of those gaps is a section of this lesson. That list, by the way, is a good way to open a conversation with a new team: here's what our pipeline proves today, and here's what it doesn't.

## Banning legacy APIs in new code

The most effective guardrail is a banned-API analyzer. The package is `Microsoft.CodeAnalysis.BannedApiAnalyzers`. You give it a text file, `BannedSymbols.txt`, listing symbols that new code must not use, and it reports rule `RS0030` wherever they appear. With `TreatWarningsAsErrors` already on, that's a build failure.

Add it once, for every project, using central package management. `Directory.Packages.props` already turns on central package management for `src` and `tests`, and a `GlobalPackageReference` there adds the analyzer to every project without touching each project file. Then include the banned-symbols file as an additional file from `Directory.Build.props`. Pin the analyzer version, and look it up on the day rather than copying one from a blog.

What goes on the list? The legacy patterns from this series. `HttpContext.Current`, from lesson eight: pass what you need explicitly. `ConfigurationManager`, from lesson nineteen: use the options pattern. `BinaryFormatter`, from lesson eighteen: removed in .NET 9, use System.Text.Json. `Encoding.Default`: name the encoding explicitly. `TransactionScope`, from lesson sixteen: no distributed transactions, use the outbox. `DateTime.Now`, from lesson six: inject a `TimeProvider`.

One entry is surprising. You might think `HttpContext.Current` can't even compile in modern .NET. But if a project references the SystemWebAdapters package, it does compile. So ban it explicitly.

And ban precisely. Banning `Math.Round` entirely would just train people to suppress the rule. Instead, ban at the overload level. The overloads that take a decimal, or a decimal and a number of digits, without a `MidpointRounding` argument, are banned, because they hide a decision. The overload that takes an explicit midpoint rounding mode is allowed. And rounding a double to two digits is banned, with the message "money is decimal". The entries use documentation comment IDs, which have a precise syntax, so check each one against the analyzer's documentation, especially for generic methods.

Every entry carries a message, and the message tells the developer what to use instead, and ideally where to read more. A build error that says "banned" is frustrating. A build error that says "inject TimeProvider, see the playbook section on time" teaches. When there's a genuine exception, the developer suppresses the rule with a written justification, and that justification is visible in review.

## Architecture tests

Some rules are about structure, not individual APIs. Those belong in architecture tests.

The most important one keeps the domain pure. `FeeBilling.Domain` must not reference EF Core, ASP.NET Core, a logging framework or `System.Web`. You don't even need a package for this. A plain test can load the domain assembly, ask for its referenced assemblies, and assert that none of their names start with a forbidden prefix. That catches the common failure, where someone adds a package to the domain project just for one attribute. For richer, type-level rules, such as "only endpoint classes may depend on the HTTP context", libraries like NetArchTest or ArchUnitNET help; check their maintenance status before you adopt one.

Two more architecture checks come straight from earlier lessons. From lesson twenty: calls to `IgnoreQueryFilters` may only appear in an allowlisted set of files, because each one bypasses tenant isolation. And every endpoint must require authorization, with the cross-tenant test walking every endpoint automatically, so a new endpoint is covered the day it's added.

## The parity gate and golden-file governance

The single most important check in the pipeline is parity. The parity test project from lesson four, `tests/FeeBilling.Parity.Tests`, doesn't exist yet; it's work package one. When it does, it becomes a required status check. No merge without it.

But a gate is only as strong as the thing it compares against. If "update the golden file" is one command anyone can run without review, the parity gate protects nothing. So govern the files that define correctness with a `CODEOWNERS` file. The golden files, and the known-differences allowlist, are owned by the billing domain owners, so changing them requires their review. Architecture decision records can be owned by the architecture group. Pair that with branch protection, which requires both the parity check and code-owner review. And every allowlist entry links to an ADR or ticket, so every accepted difference has a reason and a decision behind it.

## Supply chain and delivery

Two more checks. First, dependencies. NuGet audit reports packages with known vulnerabilities. Set its mode and severity level explicitly, rather than relying on defaults that change between SDK versions. There's a trap here: with `TreatWarningsAsErrors` on, a vulnerability advisory published overnight can turn every open pull request red, even ones that didn't touch dependencies. So decide the policy deliberately. One sensible option: pull requests fail only on vulnerabilities in newly added direct dependencies, and a scheduled job fails on everything and raises tickets.

Licences are part of the supply chain too. MediatR and AutoMapper changed their licence terms in 2025; as of September 2026, check the current terms. A regulated vendor reviews licences when adding packages, not after an audit finds them.

Second, delivery. The .NET SDK can build container images directly with `dotnet publish` and the PublishContainer target, without a Dockerfile, which makes every new service containerized by default.

## Measuring progress

How do you measure migration progress? Not by lines of code rewritten or files touched. Measure things that reflect risk moving out of legacy. Traffic on the gateway's fallback route, which should trend to zero. The count of legacy API usages over time. The number of slices cut over. The number of open parity differences. And the number of firms on the modern engine.

The legacy API count is easy to automate. A short script in CI runs `git grep` for each pattern, such as `HttpContext.Current`, `ConfigurationManager`, `BinaryFormatter`, `TransactionScope`, `System.Drawing` and `DateTime.Now`, across the legacy and source folders, and writes a table to the job summary on every run. Watching `HttpContext.Current` fall from dozens to zero is a progress chart that leadership understands. And because it runs on every build, a sudden jump back up shows you exactly which pull request reintroduced a legacy pattern.

## Enablement assets

Guardrails stop the wrong thing. Enablement makes the right thing easy. Here are the assets, and why each works.

A reference vertical slice. People copy what exists, so what exists must be correct. `src/FeeBilling.Accounts.Api` and its test project are the natural seed: an endpoint group, EF Core, the shared service defaults, a Testcontainers fixture, and a test authentication handler. But it ships two bugs from earlier lessons: dates returned without a UTC marker, from lesson eleven, and a firm ID taken from the query string, from lesson twenty. The reference implementation is the most-copied code in the company, so fix those first, and add contract, integration and cross-tenant tests.

A `dotnet new` template for a new FeeBilling service. It generates a service already wired for the service defaults, ProblemDetails, authentication, health checks and OpenAPI, plus a Testcontainers test project. The right defaults cost nothing to adopt.

An ADR template and a decision log in `docs/adr`, in the format the team already uses in `docs/adr/0001-strangler-fig-migration.md`: status, date, deciders, context, decision and consequences, plus rules for superseding. Decisions stop being re-argued in pull requests.

A migration playbook: a per-slice checklist, covering inventory, capturing the contract, parity and contract tests, implementing, routing, shadowing, cutting over and deleting the legacy path, plus the traps list from this series. A checklist beats memory.

A pull request template with the same checklist as tick boxes, linking to the parity report. Review becomes verification instead of archaeology.

A pairing model. Pair on each developer's first slice, review their second, then spot-check. Knowledge transfers through doing, and then it scales. If every slice needs your personal review, you haven't enabled anyone; you've become the bottleneck.

And instructions for AI coding assistants, containing the playbook's rules, placed where the assistants read them. In this repository, `AGENTS.md` is generated, so you change the generator's input, not the file. Assistants should follow the same guardrails as people.

Even local setup is part of this. `tools/FeeBilling.DbInit` creates and seeds the databases with one command. Making the right way easy starts on day one.

## Presenting it in the interview

The training plan's ten-minute project walkthrough, in section twelve point one, ends with one minute on enablement. That minute is where many candidates run out of things to say, or fall back on generalities like "I'd document things". Don't. Name three concrete assets and one guardrail, each with a reason.

For example: "I'd fix Accounts API first and make it the reference slice, because whatever exists gets copied. I'd publish a service template, so every new service starts with health checks, telemetry, auth and a Testcontainers test project. I'd keep an ADR log, so decisions don't get re-argued in pull requests. And I'd add a banned-API analyzer, with messages that say what to use instead, so the traps we found fail the build instead of relying on review."

Then close with how you'd scale yourself: pair on each developer's first slice, review the second, and step back. That answer shows that you think about the team, not just the code, which is exactly what the SME part of the role is testing.

## Traps

`continue-on-error` as a permanent state. Quarantine explicitly and gate on the rest.

Banning too broadly, which trains people to suppress rules. Ban the overloads that hide a decision, and explain the alternative.

Silenced warnings spreading from legacy into new code.

A reference implementation with bugs, copied into every new service.

Vulnerability audits that fail unrelated pull requests, because nobody decided where they should block.

Golden files anyone can regenerate without owner review.

Measuring lines of code instead of traffic moved and legacy APIs removed.

And the subject-matter expert as a bottleneck.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How would you make other developers productive on the migration?

[pause 5s]

Make the right way the easy way. A reference vertical slice, end to end, with its known bugs fixed first, because it will be copied. A template that generates a new service already wired for health checks, telemetry, authentication and tests. An ADR log so decisions aren't re-argued. A playbook with a per-slice checklist and the traps list. And pairing on each developer's first slice, then reviewing their second, then stepping back.

**Interviewer:** How do you stop people from reintroducing legacy patterns?

[pause 5s]

Automation, not review comments. A banned-API analyzer with a message on every entry that says what to use instead, banning at the overload level where it matters, like rounding without an explicit midpoint mode. Architecture tests that keep the domain free of infrastructure and make tenant-filter bypasses visible. A parity gate. And code owners on the files that define correctness. Exceptions are allowed, but they need a written justification that's visible in review.

**Interviewer:** What goes in CI for a migration?

[pause 5s]

Both stacks building, each on the right operating system. The modern suite running against real infrastructure with Testcontainers. A trusted, gating subset of the legacy tests, with the rest explicitly quarantined. Parity tests as a required check, with golden files under code-owner review. Analyzers and architecture tests. Dependency and licence checks, with a deliberate policy for where vulnerabilities block. And a burn-down report so progress is visible on every run.

**Interviewer:** How do you measure migration progress?

[pause 5s]

Not percent of code rewritten. I'd measure traffic on the gateway's fallback route trending to zero, the count of legacy API usages over time, the number of slices cut over, open parity differences, and the number of firms billed by the modern engine. Those track risk leaving the legacy system, which is what the business actually cares about.

## Recap

Five things to remember from this lesson, and from the series.

One: every trap should be impossible to write, fail CI, or show up in a report. Knowledge that only lives in review comments gets reintroduced.

Two: turn an untrusted suite into a trusted one by quarantining explicitly and gating on the rest.

Three: ban legacy APIs precisely, with messages that teach, and protect correctness with a parity gate and code owners.

Four: measure progress by risk moved: fallback traffic, legacy API counts, slices and firms cut over.

Five: enablement means a correct reference slice, a template, an ADR log, a playbook, and pairing that scales you out of the critical path.

That's the end of the series. You now have a strategy, a proof method, and a fix for every silent killer in FeeBilling. The last step is yours: rehearse the ten-minute project walkthrough from section twelve point one of the training plan out loud, until you can tell the whole story, from the golden master to enablement, in ten minutes.
