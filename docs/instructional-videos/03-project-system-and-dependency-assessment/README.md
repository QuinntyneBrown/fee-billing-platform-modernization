# 03 · Project System and Dependency Assessment

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work packages:** prerequisite for all · **Prerequisites:** 01, 02

## Why this video exists

Before you move a single line of code, you have to know what you're standing on: how the projects build, which packages they pull in, which of those can't run on modern .NET, which are vulnerable, and which have licence terms you can't accept. Interviewers probe this with "How would you assess a Framework codebase?" and "What does Upgrade Assistant actually do?". This video does the assessment live on FeeBilling. Its first restore already prints a critical vulnerability in the logging library.

## Learning objectives

By the end, the viewer can:

- Explain the differences between old-style `.csproj` + `packages.config` and SDK-style projects with `PackageReference`, and what breaks during conversion.
- Structure shared build settings with `Directory.Build.props` and Central Package Management (`Directory.Packages.props`), including how a subtree opts out.
- Triage every legacy package into keep, upgrade, replace or remove, with a reason.
- Run a vulnerability and licence audit, and decide what fails the build.
- Inventory Framework-only API usage from the source.
- Say precisely what migration tooling automates, and what it can't.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do you assess a .NET Framework codebase before migrating it? | Project and package inventory, then Framework-only API usage (`System.Web`, WCF server, `BinaryFormatter`, `System.Drawing`, `AppDomain`, remoting, MSDTC), then vulnerability and licence audit, then a risk-ranked plan. Output: a dependency triage table and a risk register, not a gut feel. |
| What does .NET Upgrade Assistant (or Copilot app modernization) do, and not do? | Automates project-file conversion, TFM and package updates, and some mechanical code fixes. It doesn't choose your architecture, replace WCF or MSDTC with the right pattern, or prove the new code behaves the same. Tools do the grunt work; people do the design and the parity proof. |
| How do you manage package versions across 20 projects? | Central Package Management: one `Directory.Packages.props` with `PackageVersion` entries, projects reference packages without versions. Optional transitive pinning to force patched transitive dependencies. Shared build settings in `Directory.Build.props`. |
| A package you depend on changed to a commercial licence. What do you do? | Check licences before adding dependencies, especially in a regulated vendor. Pin or replace. MediatR and AutoMapper moved to commercial licences in 2025; a CQRS dispatcher is about 50 lines of your own code. |
| What's the risk in converting `packages.config` to `PackageReference`? | `install.ps1` scripts and content-file transforms don't run under `PackageReference`, so packages that edited `Web.config` on install stop doing so. Transitive dependencies change from explicit to implicit. Binding redirects need to be regenerated. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/README.md` ("How this differs from the real thing") | Real solution: old-style csproj + `packages.config`. This repo: SDK-style `net472` projects compiled against `Microsoft.NETFramework.ReferenceAssemblies`, EDMX split target, T4 removed |
| `legacy/FeeBilling.Data/FeeBilling.Data.csproj` | The `SplitEdmx` target: `XmlPeek` replacing Visual Studio's `EntityDeploy` task |
| `legacy/FeeBilling.Core/FeeBilling.Core.csproj` | SDK-style `net472`, pinned `PackageReference` versions, `<Reference Include="System.Web" />` |
| `legacy/Directory.Build.props` | `LangVersion 7.3`, `TreatWarningsAsErrors false`, `AnalysisLevel none`, `NoWarn` including `NU1701`, `AutoGenerateBindingRedirects` |
| `legacy/Directory.Packages.props` | `ManagePackageVersionsCentrally=false`: legacy opts out of CPM |
| `Directory.Build.props`, `Directory.Packages.props` | Modern defaults: `Nullable`, `TreatWarningsAsErrors`, `AnalysisLevel latest`; central versions grouped by concern |
| `legacy/FeeBilling.Web/FeeBilling.Web.csproj` | MVC 5, Web API 2, Unity, Newtonsoft 11.0.2, SystemWebAdapters.FrameworkServices |
| `legacy/FeeBilling.Invoicing/FeeBilling.Invoicing.csproj` | `PDFsharp 1.50.5147`: the GDI+ build |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Run `dotnet restore legacy/FeeBilling.sln`. `NU1904: Package 'log4net' 2.0.8 has a known critical severity vulnerability`. The assessment has started whether you planned it or not. |
| 01:30–05:00 | Old-style vs SDK-style | Explicit `<Compile Include>` lists, `HintPath` into `..\packages`, `packages.config`, hand-maintained binding redirects. SDK-style: globbing, `PackageReference` with transitive restore, reference assemblies from NuGet. Show what this repo had to do to build the legacy solution with the CLI (EDMX split, T4 removed). |
| 05:00–08:00 | Build infrastructure | `Directory.Build.props` is found by walking up from each project, and the nearest one wins: legacy's file deliberately doesn't import the root one. CPM with `PackageVersion`; legacy opts out. Modern code has `TreatWarningsAsErrors`, so an audit warning there breaks the build. Legacy's `NoWarn` hides `NU1701`, the warning that says a package was restored for a framework it doesn't target. |
| 08:00–12:30 | Dependency triage | Build the table below live, one package at a time: who uses it, does it run on .NET 10, what's the decision and why. |
| 12:30–15:00 | Security and licences | NuGet audit: `NU1901` low → `NU1904` critical, direct vs transitive (`NuGetAuditMode`). Decide the policy: fail modern builds on high/critical, fix legacy's critical findings now because it's still in production. Licence check: MediatR, AutoMapper (commercial since 2025), PDF and imaging libraries (video 21). |
| 15:00–17:30 | API usage inventory | The `git grep` from the demo, grouped: web stack (`System.Web`, `HttpContext.Current`), hosting (`ServiceBase`, WCF `ServiceContract`), data (`TransactionScope`, EDMX), serialization (`BinaryFormatter`), platform (`System.Drawing`, `Encoding.Default`), config (`ConfigurationManager`). Each group maps to a later video. |
| 17:30–19:00 | What tools do and don't do | .NET Upgrade Assistant and GitHub Copilot app modernization: csproj conversion, TFM bumps, package updates, some code fixes. Not: choosing CoreWCF vs REST, replacing MSDTC with an outbox, proving fee parity. Check the current status and recommended tool before recording. |
| 19:00–20:00 | Recap | Output of an assessment: dependency triage table, API inventory, audit policy, and a risk register (video 01). Then, and only then, a plan. |

### Dependency triage (legacy)

| Package | Version | Used by | Runs on .NET 10? | Decision |
|---|---|---|---|---|
| `EntityFramework` | 6.4.4 | Data, Core, Web, BillingRunner, Ingestion, Tests | EF 6.3+ runs on modern .NET, but the EDMX designer and `EntityDeploy` build step don't support SDK-style/.NET projects (verify current status) | Replace with EF Core 10 (WP-06, video 13). EF6-on-.NET is a possible stepping stone, not the destination. |
| `log4net` | 2.0.8 | Web, Core, BillingRunner, Ingestion, Invoicing | Newer versions do; this one is flagged critical (`GHSA-2cwj-8chv-9pp9`) and moderate (`GHSA-4f7c-pmjv-c25w`) | Upgrade in legacy **now** (it's in production). Replace with `ILogger<T>` in modern code (WP-07, video 19). |
| `Microsoft.AspNet.Mvc`, `Microsoft.AspNet.WebApi` | 5.2.7 | Web | No: built on `System.Web` | Rewrite endpoints on ASP.NET Core (WP-03, video 07). |
| `Newtonsoft.Json` | 11.0.2 | Web | Yes, but flagged high (`GHSA-5crp-9r3c-p9vr`) | Bump in legacy (fixed in 13.0.1; verify). Modern APIs use `System.Text.Json` configured for PascalCase (video 11). |
| `Unity`, `Unity.Mvc`, `Unity.AspNet.WebApi` | 5.11.1 | Web | The MVC/Web API integrations are `System.Web`-only | Replace with `Microsoft.Extensions.DependencyInjection` (video 08). |
| `PDFsharp` | 1.50.5147 | Invoicing | No: the GDI+ build depends on `System.Drawing` | Evaluate cross-platform PDF options and their licences (video 21). |
| `Microsoft.AspNetCore.SystemWebAdapters.FrameworkServices` | 2.3.0 | Web | Framework side of the bridge | Transitional by design. Remove at decommission (WP-10). |
| `MSTest.TestFramework`, `MSTest.TestAdapter` | 2.2.10 | Tests | Old but irrelevant | Leave legacy tests alone; new parity tests are xUnit v3 in the modern solution (video 04). |

### Before and after

Old-style project file (illustrative; the real one isn't in this repo):

```xml
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="log4net">
      <HintPath>..\packages\log4net.2.0.8\lib\net45\log4net.dll</HintPath>
    </Reference>
    <Compile Include="FeeCalculator.cs" />
    <Compile Include="BlendedFeeCalculator.cs" />
    <!-- ...one line per file... -->
  </ItemGroup>
</Project>
```

SDK-style, as found (`legacy/FeeBilling.Core/FeeBilling.Core.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <RootNamespace>FeeBilling.Core</RootNamespace>
    <AssemblyName>FeeBilling.Core</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="EntityFramework" Version="6.4.4" />
    <PackageReference Include="log4net" Version="2.0.8" />
  </ItemGroup>
```

Central Package Management, as found (`Directory.Packages.props`, excerpt):

```xml
<PropertyGroup>
  <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
</PropertyGroup>
<ItemGroup Label="Data">
  <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.12" />
```

Sketch of an explicit audit policy for the modern code (add to the root `Directory.Build.props`):

```xml
<PropertyGroup>
  <NuGetAudit>true</NuGetAudit>
  <NuGetAuditMode>all</NuGetAuditMode>      <!-- direct and transitive -->
  <NuGetAuditLevel>moderate</NuGetAuditLevel>
</PropertyGroup>
```

Defaults for `NuGetAuditMode` have changed between SDK versions; setting it explicitly removes the ambiguity.

## Demo

```bash
# 1. The restore is the first audit
dotnet restore legacy/FeeBilling.sln            # NU1902 / NU1903 / NU1904 warnings

# 2. Package inventory (the noun-first "dotnet package list" form also exists in newer SDKs)
dotnet list legacy/FeeBilling.sln package --include-transitive
dotnet list legacy/FeeBilling.sln package --vulnerable --include-transitive
dotnet list legacy/FeeBilling.sln package --outdated
dotnet list FeeBilling.Modern.slnx package --vulnerable --include-transitive

# 3. Framework-only API usage, grouped by the video that fixes it
git grep -nE "System\.Web|HttpContext\.Current|HttpRuntime\.Cache" -- legacy          # 07, 08, 10
git grep -nE "ServiceBase|ServiceContract|OperationContract" -- legacy                # 15, 17
git grep -nE "TransactionScope|\.edmx|SqlDataAdapter|DataSet" -- legacy               # 13, 16
git grep -nE "BinaryFormatter|Encoding\.Default|decimal\.Parse|DateTime\.Parse" -- legacy   # 18
git grep -nE "System\.Drawing|ConfigurationManager|log4net" -- legacy                 # 19, 21
```

The `--vulnerable` and `--outdated` listings need network access to nuget.org. If it's unavailable, the audit warnings from step 1 still work from the restore.

## Traps to call out

- **Treating the conversion as the migration.** Converting to SDK-style `net472` makes the build modern; it doesn't move anything to .NET 10.
- **Losing `packages.config` behaviour silently.** Content files and `install.ps1` scripts don't run under `PackageReference`. Visual Studio's built-in migration also doesn't support every project type, including classic ASP.NET web projects (verify before recording).
- **Hiding the signal.** Legacy's `NoWarn` suppresses `NU1701`. In an assessment, that warning tells you which packages are running on a framework they weren't built for.
- **Auditing only direct dependencies.** Most vulnerable code arrives transitively. Use `--include-transitive` and `NuGetAuditMode=all`.
- **Deferring legacy security fixes "because we're migrating".** Legacy is production for months or years yet. A critical log4net advisory gets patched now.
- **Adding a package without checking its licence.** MediatR and AutoMapper are the well-known 2025 examples; imaging and PDF libraries (video 21) are the next ones to check.
- **Expecting a tool to make architecture decisions.** Say what the tool did and what you did.

## Key terms

SDK-style project · `packages.config` / `PackageReference` · reference assemblies · `Directory.Build.props` · Central Package Management · transitive pinning · binding redirect · NuGet audit (`NU1901`–`NU1904`) · `NU1701` · dependency triage · .NET Upgrade Assistant · GitHub Copilot app modernization

## After the video

1. Complete the dependency triage table for `tools/` and `tests/` as well, and add a licence column.
2. Add an explicit audit policy to the root `Directory.Build.props` on a branch, run `dotnet build FeeBilling.Modern.slnx`, and decide what should fail CI.
3. Turn the `git grep` output into counts per group. That's the size estimate for each later work package.

## References

- `legacy/README.md`, `legacy/Directory.Build.props`, `legacy/Directory.Packages.props`
- `docs/brasswick-modernization-training-plan.md`: Section 2 (tooling, licensing) and Section 13 (cheat sheet)
- Microsoft Learn: *Migrate from packages.config to PackageReference*, *Central Package Management*, *Auditing package dependencies for security vulnerabilities*
- Microsoft Learn: *.NET Upgrade Assistant overview*, and the GitHub Copilot app modernization documentation (check current status)
