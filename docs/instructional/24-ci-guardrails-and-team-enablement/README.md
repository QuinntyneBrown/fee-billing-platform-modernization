# 24 · CI Guardrails and Team Enablement

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work packages:** all (cross-cutting) · **Prerequisites:** 01, 04; ideally the whole series

**Video:** [24-ci-guardrails-and-team-enablement.mp4](24-ci-guardrails-and-team-enablement.mp4) · [Slides](slides.html) · **Audio lesson:** [24-ci-guardrails-and-team-enablement.mp3](24-ci-guardrails-and-team-enablement.mp3) · [Transcript](script.md)

## Why this video exists

"SME" means other people learn from you. The FeeBilling migration will be finished by a team, not by one person, and most of that team won't have watched videos 01 to 23. Your job is to make the right way the easy way, and to make the wrong way fail CI before it reaches review. This video turns the traps from the series into automated guardrails (banned-API analyzers, architecture tests, a parity gate) and into enablement assets: a reference slice, a service template, an ADR log and a playbook. It's also the answer to the interview question aimed at the "SME" in the job title.

## Learning objectives

By the end, the viewer can:

- Read the current CI pipeline critically, and turn an untrusted legacy suite into a trusted, gating subset.
- Ban legacy APIs in new code with `Microsoft.CodeAnalysis.BannedApiAnalyzers`, including overload-level bans such as `Math.Round` without `MidpointRounding`.
- Write architecture tests that keep the domain pure and make tenant-filter bypasses visible.
- Make the parity test a required check, and govern golden files and the known-differences allowlist with code owners.
- Add supply-chain checks (NuGet audit, licence review) without making builds flaky.
- Produce enablement assets (reference slice, `dotnet new` template, ADR template, playbook, PR checklist) and measure progress with a burn-down report.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How would you make other developers productive on the migration? | A reference vertical slice, end to end, with its bugs fixed first because it will be copied. A template that generates a new service already wired for health, telemetry, auth and tests. An ADR log. A playbook with the traps list. Pairing on each developer's first slice, then review. Make the right way the easy way. |
| How do you stop people reintroducing legacy patterns? | Automation, not review comments. Banned-API analyzers with an explanation in every message, architecture tests, a parity gate, and code owners on the files that define correctness. Exceptions are allowed but must be justified and reviewed. |
| What goes in CI for a migration? | Both stacks building. The modern suite with real infrastructure (Testcontainers). A trusted legacy subset that gates. Parity tests as a required check. Analyzers and architecture tests. Dependency and licence checks. A burn-down report so progress is visible. |
| How do you measure migration progress? | Not "percent of code rewritten". Measure traffic on the gateway's fallback route, the count of legacy API usages over time, slices cut over, parity differences open, and firms on the modern engine. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `.github/workflows/ci.yml` | `legacy` on `windows-latest` with `continue-on-error: true`; `modern` on `ubuntu-latest` with Testcontainers |
| `Directory.Build.props` | `Nullable`, `TreatWarningsAsErrors`, `AnalysisLevel latest` for `src/` and `tests/` |
| `legacy/Directory.Build.props` | `AnalysisLevel none`; `NoWarn` including `CS0618` (obsolete-API warnings silenced) |
| `Directory.Packages.props` | Central package management: where analyzer packages go |
| `legacy/FeeBilling.Tests/Core/FeeCalculatorTests.cs` | `[Ignore("Flaky - cache from previous test")]` and friends: why nobody trusts the suite |
| `src/FeeBilling.Accounts.Api/` and `tests/FeeBilling.Accounts.Api.Tests/` | The seed of a reference slice: endpoint group, EF Core, ServiceDefaults, Testcontainers fixture, `TestAuthHandler` |
| `docs/adr/0001-strangler-fig-migration.md` | The ADR format the team already uses: Status, Date, Deciders, Context, Decision, Consequences |
| `tools/FeeBilling.DbInit/` | Local setup is one command: part of "easy to do the right thing" |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | The migration will be done by people who haven't seen your list of traps. Every trap in this series should either be impossible to write, fail CI, or show up in a report. |
| 01:30–04:30 | CI today | Two jobs, two operating systems: correct. But the legacy tests have `continue-on-error: true`, so they gate nothing. Fix: tag the 9 failing and 22 ignored tests with a `Quarantine` category and gate on the rest, turning an untrusted suite into a small trusted one. `legacy/Directory.Build.props` silences `CS0618`, so obsolete-API warnings in legacy are invisible. The modern job already runs real SQL Server via Testcontainers and treats warnings as errors. Build on that. |
| 04:30–08:30 | Banned APIs | `Microsoft.CodeAnalysis.BannedApiAnalyzers` plus `BannedSymbols.txt` (rule `RS0030`). Add it to every project once via `GlobalPackageReference`. Ban at overload level: `Math.Round(decimal, int)` is banned, `Math.Round(decimal, int, MidpointRounding)` isn't. `System.Web.HttpContext.Current` *does* compile in `src/` if a project references SystemWebAdapters, so ban it explicitly. Every entry carries a message that tells the developer what to use instead. Justified exceptions use a `SuppressMessage` with a justification, visible in review. |
| 08:30–11:00 | Architecture tests | `FeeBilling.Domain` references no EF Core, ASP.NET Core, logging framework or `System.Web`: a zero-package test on referenced assemblies, or NetArchTest/ArchUnitNET for richer rules. `IgnoreQueryFilters()` only appears in allowlisted files. Every endpoint requires authorization, and the cross-tenant test walks all endpoints (video 20). |
| 11:00–13:00 | The parity gate | `tests/FeeBilling.Parity.Tests` (future, WP-01) is a required status check. Golden files and the known-differences allowlist are owned via `CODEOWNERS`, so changing them needs the billing domain owners. Each allowlist entry links to an ADR or ticket. |
| 13:00–14:30 | Supply chain and delivery | NuGet audit: set `NuGetAuditMode` and `NuGetAuditLevel` explicitly. With `TreatWarningsAsErrors`, a newly published advisory can break an unrelated PR, so decide the policy: fail PRs only on new direct dependencies, and fail a scheduled job on everything. Licence review when adding packages (MediatR and AutoMapper changed licence terms in 2025; verify current terms). Container images with `dotnet publish /t:PublishContainer`. |
| 14:30–15:30 | Burn-down report | A script counts legacy API usages per pattern and writes a table to the job summary on every run. Watching `HttpContext.Current` fall from N to 0 is the progress chart leadership understands. |
| 15:30–19:00 | Enablement assets | **Reference slice:** Accounts.Api is the seed, but it ships the handover's `DateTime` bug and a tenant hole, and the reference implementation is the most copied code in the company, so fix it first. **Template:** `dotnet new feebilling-service` generates a service with ServiceDefaults, ProblemDetails, auth, health and a Testcontainers test project. **ADR template and log** in the existing format. **Playbook:** a per-slice checklist plus the traps list from this series. **PR template** with the checklist. **Pairing:** each developer's first slice is paired, then reviewed. |
| 19:00–20:00 | Recap | Guardrails turn knowledge into CI failures. Enablement turns it into defaults. Present both in the last minute of the 10-minute walkthrough (training plan Section 12.1, step 7). |

### Before: the suite that gates nothing

```yaml
      - name: Test
        run: dotnet test legacy/FeeBilling.Tests -c Release --no-build
        # Known failures (9) and ignored tests (22). Nobody trusts this suite; it doesn't gate merges.
        continue-on-error: true
```

```xml
    <AnalysisLevel>none</AnalysisLevel>
    <NoWarn>$(NoWarn);NU1701;CS0618;CS0612;CS1591</NoWarn>
```

### After: a trusted legacy subset (sketch)

```yaml
      - name: Test (trusted subset, gating)
        run: dotnet test legacy/FeeBilling.Tests -c Release --no-build --filter "TestCategory!=Quarantine"
```

Each quarantined test gets `[TestCategory("Quarantine")]` and a ticket. The quarantine list only shrinks. Superseding these tests with the golden master (WP-01) is how most of them leave.

### After: banning legacy APIs in new code (sketch)

```xml
<!-- Directory.Packages.props: applies to every project under src/ and tests/ -->
<ItemGroup>
  <GlobalPackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="x.y.z" />
</ItemGroup>

<!-- Directory.Build.props -->
<ItemGroup>
  <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
</ItemGroup>
```

```text
P:System.Web.HttpContext.Current;Pass what you need explicitly. See playbook: ambient state (video 08)
T:System.Configuration.ConfigurationManager;Use IOptions<T> (video 19)
T:System.Runtime.Serialization.Formatters.Binary.BinaryFormatter;Removed in .NET 9. Use System.Text.Json (video 18)
P:System.Text.Encoding.Default;Name the encoding explicitly (video 18)
T:System.Transactions.TransactionScope;No distributed transactions. Use the outbox (video 16)
P:System.DateTime.Now;Inject TimeProvider (video 06)
M:System.Math.Round(System.Decimal);Specify MidpointRounding explicitly (video 05)
M:System.Math.Round(System.Decimal,System.Int32);Specify MidpointRounding explicitly (video 05)
M:System.Math.Round(System.Double,System.Int32);Money is decimal (video 05)
```

The entries use documentation-comment IDs. Check each ID against the analyzer's documentation, especially generic methods such as `IgnoreQueryFilters`, before recording. Pin the analyzer version in `Directory.Packages.props` and look it up on the day; don't copy `x.y.z`.

### After: architecture tests (sketch)

```csharp
[Fact]
public void Domain_ReferencesNoInfrastructure()
{
    var forbidden = new[] { "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "log4net", "System.Web" };

    var offenders = typeof(FeeBilling.Domain.ValueObjects.BillingPeriod).Assembly
        .GetReferencedAssemblies()
        .Where(reference => forbidden.Any(prefix => reference.Name!.StartsWith(prefix, StringComparison.Ordinal)))
        .Select(reference => reference.Name)
        .ToList();

    Assert.Empty(offenders);
}
```

This needs no extra package, and it catches the common failure: someone adds a package reference to the domain project "just for one attribute". Use NetArchTest or ArchUnitNET (check maintenance status) when you need type-level rules, such as "only `*Endpoints` classes may depend on `HttpContext`".

### After: golden-file governance (sketch)

```text
# .github/CODEOWNERS
/golden/                                     @feebilling/billing-domain-owners
/tests/FeeBilling.Parity.Tests/KnownDifferences.json   @feebilling/billing-domain-owners
/docs/adr/                                   @feebilling/architecture
```

Team names are placeholders. Pair `CODEOWNERS` with branch protection that requires the parity check and code-owner review.

### After: the burn-down report (sketch)

```bash
{
  echo "| Legacy API | Usages (legacy + src) |"
  echo "|---|---|"
  for pattern in 'HttpContext\.Current' 'ConfigurationManager' 'BinaryFormatter' 'TransactionScope' \
                 'System\.Drawing' 'Encoding\.Default' 'HttpRuntime\.Cache' 'DateTime\.Now'; do
    count=$(git grep -cE "$pattern" -- legacy src | awk -F: '{ total += $NF } END { print total + 0 }')
    echo "| \`$pattern\` | $count |"
  done
} >> "$GITHUB_STEP_SUMMARY"
```

### Enablement assets

| Asset | What it contains | Why it works |
|---|---|---|
| Reference slice | Accounts.Api, fixed (UTC dates, tenant from claims), with contract, integration and cross-tenant tests | People copy what exists. Make what exists correct. |
| `dotnet new` template | A new service with ServiceDefaults, ProblemDetails, auth, health, OpenAPI and a Testcontainers test project (`.template.config/template.json`) | The right defaults cost nothing to adopt |
| ADR template and log | `docs/adr/`, in the format of ADR-0001 to ADR-0004; superseding rules | Decisions stop being re-litigated in PRs |
| Migration playbook | Per-slice checklist: inventory, capture contract, parity/contract tests, implement, route, shadow, cut over, delete the legacy path; plus the traps list | A checklist beats memory |
| PR template | The same checklist as tick boxes, with links to parity reports | Review becomes verification, not archaeology |
| Pairing model | Pair on each developer's first slice, review the second, then spot-check | Knowledge transfers through doing, then scales |
| Assistant instructions | The playbook's rules, where AI coding assistants read them (this repo's `AGENTS.md` is generated, so change the generator's input) | Assistants follow the same guardrails as people |

## Demo

```bash
# What gates a merge today, and what doesn't
git grep -n "continue-on-error" -- .github
git grep -n "TreatWarningsAsErrors\|AnalysisLevel\|NoWarn" -- Directory.Build.props legacy/Directory.Build.props

# Baseline for the burn-down chart
for p in 'HttpContext\.Current' 'ConfigurationManager' 'BinaryFormatter' 'TransactionScope' 'DateTime\.Now'; do
  printf '%-24s %s\n' "$p" "$(git grep -cE "$p" -- legacy src | awk -F: '{ t += $NF } END { print t + 0 }')"
done

# The trusted legacy subset, once the quarantine category exists
dotnet test legacy/FeeBilling.Tests -c Release --no-build --filter "TestCategory!=Quarantine"
```

## Traps to call out

- **`continue-on-error` as a permanent state.** A test job that can't fail is decoration. Quarantine explicitly, and gate on the rest.
- **Banning too broadly.** Banning `Math.Round` entirely just trains people to suppress the rule. Ban the overloads that hide a decision, and explain the alternative in the message.
- **Silenced warnings.** `NoWarn` on `CS0618` in legacy hides every `[Obsolete]` API. Don't let a copy of that line reach `src/`.
- **A reference implementation with bugs.** Accounts.Api's `DateTime` kind and tenant handling will be copied into every new service unless they're fixed first.
- **Audit warnings that fail unrelated PRs.** With `TreatWarningsAsErrors`, a new advisory published overnight turns every PR red. Decide deliberately where vulnerability findings block.
- **Golden files anyone can regenerate.** If "update the snapshot" is one command with no owner review, the parity gate protects nothing.
- **Measuring lines of code.** Progress is traffic moved, legacy APIs removed and firms cut over, not files touched.
- **The SME as bottleneck.** If every slice needs your review, you haven't enabled anyone. Pair, then step back.

## Key terms

Required status check · branch protection · `CODEOWNERS` · banned-API analyzer (`RS0030`) · documentation-comment ID · `GlobalPackageReference` · architecture test · quarantine · NuGet audit · SDK container publishing · reference implementation · `dotnet new` template · playbook · burn-down

## After the video

1. Write `BannedSymbols.txt` for `src/` with a message for every entry, and list the documentation IDs you had to look up.
2. Tag the legacy test suite's failing and ignored tests with a quarantine category, and make the rest gate the build.
3. Rehearse step 7 of the 10-minute walkthrough (training plan Section 12.1): enablement in 60 seconds, naming three concrete assets.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 1 ("Enablement" row), Section 11 (commit discipline), Section 12.1 (walkthrough step 7), Section 12.2 (the SME question)
- `.github/workflows/ci.yml`, `Directory.Build.props`, `Directory.Packages.props`, `legacy/Directory.Build.props`
- Microsoft Learn: *Central Package Management* (`GlobalPackageReference`), *Auditing package dependencies for security vulnerabilities*, *Containerize a .NET app with dotnet publish*, *Custom templates for dotnet new*
- Roslyn analyzers repository: *BannedApiAnalyzers* documentation
- GitHub Docs: *About code owners*, *About protected branches*
