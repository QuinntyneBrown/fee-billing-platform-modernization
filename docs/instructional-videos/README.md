# Instructional Videos: Migrating FeeBilling from .NET Framework to Modern .NET

A series of ~20-minute, code-first technical videos on the FeeBilling migration from .NET Framework 4.7.2 to .NET 10. Each video takes one migration problem, shows where it lives in this repository, explains the fix, and rehearses the interview questions it prepares you for.

> **".NET Core" vs ".NET".** Microsoft dropped the "Core" name at .NET 5. "Migrating to .NET Core" today means migrating to modern, cross-platform .NET. The target here is **.NET 10 (LTS, supported until November 2028)**. Saying this precisely in an interview is a small signal that you're current.

## How each video folder is organized

Every folder has a `README.md` with the same sections:

| Section | Purpose |
|---|---|
| Why this video exists | The interview capability, and the FeeBilling problem that demonstrates it |
| Learning objectives | What the viewer can do afterwards |
| Interview questions this prepares you for | Likely questions, with what a strong answer includes |
| FeeBilling code on screen | Exact files to open, and what to point at |
| Run sheet | Time-boxed segments adding up to ~20 minutes |
| Demo | Commands and steps to run live |
| Traps to call out | The mistakes that separate "read about it" from "did it" |
| Key terms, After the video, References | Vocabulary, a follow-up exercise, and sources |

"Before" code in the videos is the legacy code **as found** in `legacy/`. "After" code is a sketch of the target. The work packages themselves (WP-01 to WP-10) are deliberately not implemented in this repository.

## The series

| # | Video | Work package | Core interview question |
|---|---|---|---|
| 01 | [Migration strategy: strangler fig vs big bang](01-migration-strategy-strangler-fig/) | All (framing) | How would you approach modernizing a large Framework app? |
| 02 | [Target frameworks and shared libraries](02-target-frameworks-and-shared-libraries/) | — | Why .NET 10? How do old and new code share a library? |
| 03 | [Project system and dependency assessment](03-project-system-and-dependency-assessment/) | — | How do you assess a Framework codebase before migrating it? |
| 04 | [Golden-master characterization testing](04-golden-master-characterization-testing/) | WP-01 | How do you know the new system is correct? |
| 05 | [Money, rounding and numeric parity](05-money-rounding-and-numeric-parity/) | WP-01, WP-02 | You find a bug in the legacy fee calculation. What do you do? |
| 06 | [A pure-domain fee engine](06-pure-domain-fee-engine/) | WP-02 | How do you make untestable static business logic testable? |
| 07 | [From System.Web to the ASP.NET Core pipeline](07-system-web-to-aspnetcore-pipeline/) | WP-03 | What replaces Global.asax, modules, handlers and Web API 2? |
| 08 | [Dependency injection and ambient state](08-dependency-injection-and-ambient-state/) | WP-02, WP-04 | How do you remove `HttpContext.Current` and static state? |
| 09 | [YARP gateway and incremental routing](09-yarp-gateway-incremental-routing/) | WP-03 | How do you migrate one route at a time? |
| 10 | [SystemWebAdapters: remote auth and session](10-systemwebadapters-remote-auth-and-session/) | WP-08 | How do old and new apps share a logged-in user? |
| 11 | [API contract parity: JSON and dates](11-api-contract-parity-json-and-dates/) | WP-03 | What breaks silently when an endpoint moves? |
| 12 | [Billing API: idempotency, CQRS and streaming](12-billing-api-idempotency-and-streaming/) | WP-03 | How do you stop a double-click from double-billing? |
| 13 | [EF6 to EF Core](13-ef6-to-ef-core/) | WP-06 | What changes between EF6 and EF Core? |
| 14 | [Shared database and schema ownership](14-shared-database-and-schema-ownership/) | WP-06 | Two ORMs, one schema: who owns changes? |
| 15 | [Windows Service to Worker Service](15-windows-service-to-worker-service/) | WP-04 | How do you replace a Windows Service safely? |
| 16 | [Replacing MSDTC: outbox and messaging](16-replacing-msdtc-with-outbox-and-messaging/) | WP-04 | What replaces a distributed transaction? |
| 17 | [WCF to CoreWCF](17-wcf-to-corewcf/) | WP-05 | How do you handle WCF when you can't change the clients? |
| 18 | [BinaryFormatter, encoding and culture](18-binaryformatter-encoding-and-culture/) | WP-05 | What breaks silently when moving to modern .NET? |
| 19 | [Configuration, logging and observability](19-configuration-logging-and-observability/) | WP-07 | What replaces Web.config, transforms and log4net? |
| 20 | [Authentication, authorization and tenancy](20-authentication-authorization-and-tenancy/) | WP-08 | How do you move from Forms auth to OIDC, and isolate tenants? |
| 21 | [System.Drawing and PDF generation](21-system-drawing-and-pdf-generation/) | — | What do you do with Windows-only dependencies? |
| 22 | [AngularJS to Angular: route-level strangler](22-angularjs-to-angular-strangler/) | WP-09 | How do you migrate the front end without a big bang? |
| 23 | [Cutover, shadow runs and decommissioning](23-cutover-shadow-runs-and-decommission/) | WP-10 | How do you cut over a billing system and roll back? |
| 24 | [CI guardrails and team enablement](24-ci-guardrails-and-team-enablement/) | All | How would you make other developers productive on the migration? |

## Suggested viewing orders

- **Full series:** 01 to 24 in order. Each video lists its prerequisites.
- **Interview in a few days:** 01, 04, 05, 16, 18, 11, then 24 for the SME question. These cover the strategy answer, the parity story, the money traps, the MSDTC replacement, the "silent breakage" list, and enablement.
- **Data-heavy role:** 13, 14, 15, 16.
- **Fullstack role:** 07, 09, 10, 11, 22.

## Before recording

- **Check time-sensitive facts.** .NET support dates, tooling status (.NET Upgrade Assistant vs GitHub Copilot app modernization) and package licences (MediatR, AutoMapper, QuestPDF, ImageSharp) change. The facts in these READMEs are as of September 2026.
- **Start from a clean environment:** `docker compose up -d`, then `dotnet run --project tools/FeeBilling.DbInit --reseed`.
- **Run the legacy demos on Windows.** They need .NET Framework at runtime; everything builds anywhere.
