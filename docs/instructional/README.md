# Instructional Series: Migrating FeeBilling from .NET Framework to Modern .NET

Twenty-four ~20-minute technical lessons on the FeeBilling migration from .NET Framework 4.7.2 to .NET 10. Each lesson takes one migration problem, shows where it lives in this repository, explains the fix, and drills the interview questions it prepares you for.

Every lesson comes in two forms:

- an **audio lesson** (MP3, about 20 minutes), written to be understood without seeing any code, ending in a spoken interview drill;
- a **video outline** (`README.md`) for recording the same material on screen.

> **".NET Core" vs ".NET".** Microsoft dropped the "Core" name at .NET 5. "Migrating to .NET Core" today means migrating to modern, cross-platform .NET. The target here is **.NET 10 (LTS, supported until November 2028)**. Saying this precisely in an interview is a small signal that you're current.

## What's in each lesson folder

| File | What it is |
|---|---|
| `NN-topic.mp3` | The audio lesson. A narrator teaches; a second voice asks the interview questions in the drill. |
| `script.md` | The transcript the audio is generated from. Read it, search it, or edit it and regenerate. |
| `README.md` | The video outline: objectives, interview questions with strong-answer notes, files to show, a timed run sheet, demo commands, traps, and references. |
| `NN-topic.mp4`, `slides.html` | The finished video (1080p, captions included) and the slide deck it's built from. Open `slides.html` in a browser and use the arrow keys to step through the deck. |

"Before" code is the legacy code **as found** in `legacy/`. "After" code is a sketch of the target. The work packages themselves (WP-01 to WP-10) are deliberately not implemented in this repository.

## Listening

- Every audio lesson has the same shape: the questions it answers, the concepts, the FeeBilling code, the modern approach, traps, an **interview drill**, and a five-point recap.
- In the drill, each question is followed by a five-second pause. Pause the player if you need longer, answer out loud, then compare your answer with the model answer.
- The MP3s are stored in **Git LFS**. After cloning, run `git lfs install` once and then `git lfs pull` to download them.

## The series

| # | Lesson | Audio and video | Work package | Core interview question |
|---|---|---|---|---|
| 01 | [Migration strategy: strangler fig vs big bang](01-migration-strategy-strangler-fig/) | [MP3](01-migration-strategy-strangler-fig/01-migration-strategy-strangler-fig.mp3) · [Video](01-migration-strategy-strangler-fig/01-migration-strategy-strangler-fig.mp4) · [Transcript](01-migration-strategy-strangler-fig/script.md) | All (framing) | How would you approach modernizing a large Framework app? |
| 02 | [Target frameworks and shared libraries](02-target-frameworks-and-shared-libraries/) | [MP3](02-target-frameworks-and-shared-libraries/02-target-frameworks-and-shared-libraries.mp3) · [Video](02-target-frameworks-and-shared-libraries/02-target-frameworks-and-shared-libraries.mp4) · [Transcript](02-target-frameworks-and-shared-libraries/script.md) | — | Why .NET 10? How do old and new code share a library? |
| 03 | [Project system and dependency assessment](03-project-system-and-dependency-assessment/) | [MP3](03-project-system-and-dependency-assessment/03-project-system-and-dependency-assessment.mp3) · [Video](03-project-system-and-dependency-assessment/03-project-system-and-dependency-assessment.mp4) · [Transcript](03-project-system-and-dependency-assessment/script.md) | — | How do you assess a Framework codebase before migrating it? |
| 04 | [Golden-master characterization testing](04-golden-master-characterization-testing/) | [MP3](04-golden-master-characterization-testing/04-golden-master-characterization-testing.mp3) · [Video](04-golden-master-characterization-testing/04-golden-master-characterization-testing.mp4) · [Transcript](04-golden-master-characterization-testing/script.md) | WP-01 | How do you know the new system is correct? |
| 05 | [Money, rounding and numeric parity](05-money-rounding-and-numeric-parity/) | [MP3](05-money-rounding-and-numeric-parity/05-money-rounding-and-numeric-parity.mp3) · [Video](05-money-rounding-and-numeric-parity/05-money-rounding-and-numeric-parity.mp4) · [Transcript](05-money-rounding-and-numeric-parity/script.md) | WP-01, WP-02 | You find a bug in the legacy fee calculation. What do you do? |
| 06 | [A pure-domain fee engine](06-pure-domain-fee-engine/) | [MP3](06-pure-domain-fee-engine/06-pure-domain-fee-engine.mp3) · [Video](06-pure-domain-fee-engine/06-pure-domain-fee-engine.mp4) · [Transcript](06-pure-domain-fee-engine/script.md) | WP-02 | How do you make untestable static business logic testable? |
| 07 | [From System.Web to the ASP.NET Core pipeline](07-system-web-to-aspnetcore-pipeline/) | [MP3](07-system-web-to-aspnetcore-pipeline/07-system-web-to-aspnetcore-pipeline.mp3) · [Video](07-system-web-to-aspnetcore-pipeline/07-system-web-to-aspnetcore-pipeline.mp4) · [Transcript](07-system-web-to-aspnetcore-pipeline/script.md) | WP-03 | What replaces Global.asax, modules, handlers and Web API 2? |
| 08 | [Dependency injection and ambient state](08-dependency-injection-and-ambient-state/) | [MP3](08-dependency-injection-and-ambient-state/08-dependency-injection-and-ambient-state.mp3) · [Video](08-dependency-injection-and-ambient-state/08-dependency-injection-and-ambient-state.mp4) · [Transcript](08-dependency-injection-and-ambient-state/script.md) | WP-02, WP-04 | How do you remove `HttpContext.Current` and static state? |
| 09 | [YARP gateway and incremental routing](09-yarp-gateway-incremental-routing/) | [MP3](09-yarp-gateway-incremental-routing/09-yarp-gateway-incremental-routing.mp3) · [Video](09-yarp-gateway-incremental-routing/09-yarp-gateway-incremental-routing.mp4) · [Transcript](09-yarp-gateway-incremental-routing/script.md) | WP-03 | How do you migrate one route at a time? |
| 10 | [SystemWebAdapters: remote auth and session](10-systemwebadapters-remote-auth-and-session/) | [MP3](10-systemwebadapters-remote-auth-and-session/10-systemwebadapters-remote-auth-and-session.mp3) · [Video](10-systemwebadapters-remote-auth-and-session/10-systemwebadapters-remote-auth-and-session.mp4) · [Transcript](10-systemwebadapters-remote-auth-and-session/script.md) | WP-08 | How do old and new apps share a logged-in user? |
| 11 | [API contract parity: JSON and dates](11-api-contract-parity-json-and-dates/) | [MP3](11-api-contract-parity-json-and-dates/11-api-contract-parity-json-and-dates.mp3) · [Video](11-api-contract-parity-json-and-dates/11-api-contract-parity-json-and-dates.mp4) · [Transcript](11-api-contract-parity-json-and-dates/script.md) | WP-03 | What breaks silently when an endpoint moves? |
| 12 | [Billing API: idempotency, CQRS and streaming](12-billing-api-idempotency-and-streaming/) | [MP3](12-billing-api-idempotency-and-streaming/12-billing-api-idempotency-and-streaming.mp3) · [Video](12-billing-api-idempotency-and-streaming/12-billing-api-idempotency-and-streaming.mp4) · [Transcript](12-billing-api-idempotency-and-streaming/script.md) | WP-03 | How do you stop a double-click from double-billing? |
| 13 | [EF6 to EF Core](13-ef6-to-ef-core/) | [MP3](13-ef6-to-ef-core/13-ef6-to-ef-core.mp3) · [Video](13-ef6-to-ef-core/13-ef6-to-ef-core.mp4) · [Transcript](13-ef6-to-ef-core/script.md) | WP-06 | What changes between EF6 and EF Core? |
| 14 | [Shared database and schema ownership](14-shared-database-and-schema-ownership/) | [MP3](14-shared-database-and-schema-ownership/14-shared-database-and-schema-ownership.mp3) · [Video](14-shared-database-and-schema-ownership/14-shared-database-and-schema-ownership.mp4) · [Transcript](14-shared-database-and-schema-ownership/script.md) | WP-06 | Two ORMs, one schema: who owns changes? |
| 15 | [Windows Service to Worker Service](15-windows-service-to-worker-service/) | [MP3](15-windows-service-to-worker-service/15-windows-service-to-worker-service.mp3) · [Video](15-windows-service-to-worker-service/15-windows-service-to-worker-service.mp4) · [Transcript](15-windows-service-to-worker-service/script.md) | WP-04 | How do you replace a Windows Service safely? |
| 16 | [Replacing MSDTC: outbox and messaging](16-replacing-msdtc-with-outbox-and-messaging/) | [MP3](16-replacing-msdtc-with-outbox-and-messaging/16-replacing-msdtc-with-outbox-and-messaging.mp3) · [Video](16-replacing-msdtc-with-outbox-and-messaging/16-replacing-msdtc-with-outbox-and-messaging.mp4) · [Transcript](16-replacing-msdtc-with-outbox-and-messaging/script.md) | WP-04 | What replaces a distributed transaction? |
| 17 | [WCF to CoreWCF](17-wcf-to-corewcf/) | [MP3](17-wcf-to-corewcf/17-wcf-to-corewcf.mp3) · [Video](17-wcf-to-corewcf/17-wcf-to-corewcf.mp4) · [Transcript](17-wcf-to-corewcf/script.md) | WP-05 | How do you handle WCF when you can't change the clients? |
| 18 | [BinaryFormatter, encoding and culture](18-binaryformatter-encoding-and-culture/) | [MP3](18-binaryformatter-encoding-and-culture/18-binaryformatter-encoding-and-culture.mp3) · [Video](18-binaryformatter-encoding-and-culture/18-binaryformatter-encoding-and-culture.mp4) · [Transcript](18-binaryformatter-encoding-and-culture/script.md) | WP-05 | What breaks silently when moving to modern .NET? |
| 19 | [Configuration, logging and observability](19-configuration-logging-and-observability/) | [MP3](19-configuration-logging-and-observability/19-configuration-logging-and-observability.mp3) · [Video](19-configuration-logging-and-observability/19-configuration-logging-and-observability.mp4) · [Transcript](19-configuration-logging-and-observability/script.md) | WP-07 | What replaces Web.config, transforms and log4net? |
| 20 | [Authentication, authorization and tenancy](20-authentication-authorization-and-tenancy/) | [MP3](20-authentication-authorization-and-tenancy/20-authentication-authorization-and-tenancy.mp3) · [Video](20-authentication-authorization-and-tenancy/20-authentication-authorization-and-tenancy.mp4) · [Transcript](20-authentication-authorization-and-tenancy/script.md) | WP-08 | How do you move from Forms auth to OIDC, and isolate tenants? |
| 21 | [System.Drawing and PDF generation](21-system-drawing-and-pdf-generation/) | [MP3](21-system-drawing-and-pdf-generation/21-system-drawing-and-pdf-generation.mp3) · [Video](21-system-drawing-and-pdf-generation/21-system-drawing-and-pdf-generation.mp4) · [Transcript](21-system-drawing-and-pdf-generation/script.md) | — | What do you do with Windows-only dependencies? |
| 22 | [AngularJS to Angular: route-level strangler](22-angularjs-to-angular-strangler/) | [MP3](22-angularjs-to-angular-strangler/22-angularjs-to-angular-strangler.mp3) · [Video](22-angularjs-to-angular-strangler/22-angularjs-to-angular-strangler.mp4) · [Transcript](22-angularjs-to-angular-strangler/script.md) | WP-09 | How do you migrate the front end without a big bang? |
| 23 | [Cutover, shadow runs and decommissioning](23-cutover-shadow-runs-and-decommission/) | [MP3](23-cutover-shadow-runs-and-decommission/23-cutover-shadow-runs-and-decommission.mp3) · [Video](23-cutover-shadow-runs-and-decommission/23-cutover-shadow-runs-and-decommission.mp4) · [Transcript](23-cutover-shadow-runs-and-decommission/script.md) | WP-10 | How do you cut over a billing system and roll back? |
| 24 | [CI guardrails and team enablement](24-ci-guardrails-and-team-enablement/) | [MP3](24-ci-guardrails-and-team-enablement/24-ci-guardrails-and-team-enablement.mp3) · [Video](24-ci-guardrails-and-team-enablement/24-ci-guardrails-and-team-enablement.mp4) · [Transcript](24-ci-guardrails-and-team-enablement/script.md) | All | How would you make other developers productive on the migration? |

## Suggested orders

- **Full series:** 01 to 24 in order. Each lesson assumes the ones before it.
- **Interview in a few days:** 01, 04, 05, 16, 18, 11, then 24 for the SME question. These cover the strategy answer, the parity story, the money traps, the MSDTC replacement, the "silent breakage" list, and enablement.
- **Data-heavy role:** 13, 14, 15, 16.
- **Fullstack role:** 07, 09, 10, 11, 22.

## Regenerating the audio

The MP3s are generated from each `script.md` by `tools/instructional-audio/generate.cs`, a .NET 10 file-based app that calls Azure AI Speech neural text to speech. The narrator is `en-US-AndrewMultilingualNeural` and the interviewer is `en-US-AvaMultilingualNeural`.

```bash
# Validate scripts and estimate length and cost. No network, no key needed.
dotnet run tools/instructional-audio/generate.cs -- --dry-run

# Synthesize one or more lessons (folder-name filters), or everything with no filter.
export AZURE_SPEECH_KEY=$(az cognitiveservices account keys list -n <speech-resource> -g <resource-group> --query key1 -o tsv)
export AZURE_SPEECH_REGION=eastus2
dotnet run tools/instructional-audio/generate.cs -- 04 05

# Hear how every term in the pronunciation lexicon is spoken.
dotnet run tools/instructional-audio/generate.cs -- --pronunciation-test
```

- **Script format:**
  - `# NN · Title` first, then `## Section` headings (one synthesis request each).
  - Plain paragraphs are read by the narrator; `**Interviewer:**` paragraphs use the second voice.
  - `[pause 5s]` inserts silence.
  - Inline code is spoken through the lexicon.
  - Tables, fenced code blocks, links and HTML are rejected, because they can't be read aloud.
- **Pronunciation:** `tools/instructional-audio/pronunciations.json` maps acronyms, file names and code to how they're spoken (`.NET` becomes "dot net", `SQL` becomes "sequel").
- **Cache:** audio is cached per section in `tools/instructional-audio/.cache/` (gitignored), so editing one section of a script only re-synthesizes that section.
- **Cost:** the whole series is roughly 450,000 characters, about $7 at $15 per million characters (as of September 2026). The dry run prints the estimate.

## Building a video

A lesson video is the lesson's narration MP3 with a slide deck timed to it. `tools/instructional-video/build.cs` screenshots each `<section>` of the lesson's `slides.html` with headless Edge or Chrome. It shows each slide from the moment the narration reaches that slide's `data-cue` phrase (a phrase from `script.md`), adds captions generated from the script, and encodes a 1080p MP4 with ffmpeg.

```bash
# The audio generator must have run for the lesson first: it writes the timing manifest the video is synced to.
dotnet run tools/instructional-audio/generate.cs -- 01

# ffmpeg needs libx264 (FFMPEG_PATH if it isn't on PATH). EDGE_PATH overrides the browser.
export FFMPEG_PATH=/path/to/ffmpeg
dotnet run tools/instructional-video/build.cs -- 01 02 03

# While editing a deck: validate cues and print the schedule, or also render the PNGs to review, without encoding.
dotnet run tools/instructional-video/build.cs -- 05 --check
dotnet run tools/instructional-video/build.cs -- 05 --slides-only
```

- **Shared look:** every deck links `assets/slides.css` and `assets/slides.js`. The script builds the header and progress bar from the `<body>` attributes, colours code blocks, and supports progressive builds from a `<template>` (`data-show`, `data-highlight`).
- **Adding a video:** write `slides.html` for the lesson, using any existing deck as the template. Give every slide except the first a `data-cue` taken verbatim from `script.md`, in narration order. The builder reports any cue it can't find.
- **Timing accuracy:** section boundaries are exact. Slide changes within a section are estimated from word counts, so they land within a second or two of the cue.

## Before recording the videos

- **Check time-sensitive facts.** .NET support dates, tooling status (.NET Upgrade Assistant vs GitHub Copilot app modernization) and package licences (MediatR, AutoMapper, QuestPDF, ImageSharp) change. The facts in these lessons are as of September 2026.
- **Start from a clean environment:** `docker compose up -d`, then `dotnet run --project tools/FeeBilling.DbInit --reseed`.
- **Run the legacy demos on Windows.** They need .NET Framework at runtime; everything builds anywhere.
