# 03 · Project System and Dependency Assessment

Welcome back. Before you move a single line of code, you need to know what you're standing on. How do the projects build? Which packages do they pull in? Which of those can't run on modern .NET, which have known vulnerabilities, and which carry licence terms your company can't accept? Interviewers probe this with questions like "How would you assess a Framework codebase?" and "What does the Upgrade Assistant actually do?" In this lesson, we'll do that assessment on FeeBilling. As you'll hear, its very first package restore already reports a critical vulnerability in the logging library.

## The questions this lesson answers

Here's what you'll be able to answer. How do you assess a .NET Framework codebase before migrating it? What does .NET Upgrade Assistant, or GitHub Copilot's app modernization tooling, do, and what doesn't it do? How do you manage package versions across twenty projects? A package you depend on changed to a commercial licence: what do you do? And what's the risk in converting `packages.config` to package references?

The theme of the whole lesson is this: the output of an assessment is a set of artifacts, not a gut feel. A dependency triage table, an inventory of Framework-only API usage, an audit policy, and a risk register. Then, and only then, a plan.

## Old-style projects versus SDK-style projects

Let's start with the project system itself, because it's the first thing a migration touches.

An old-style .NET Framework project file is verbose. It lists every source file explicitly, one `Compile` item per file. It references packages through `HintPath` entries that point into a `packages` folder next to the solution. The package list itself lives in a separate file called `packages.config`. Transitive dependencies aren't resolved for you: every package your packages need is listed explicitly. And binding redirects in the config file are often maintained by hand.

An SDK-style project is much smaller. Source files are included by globbing, so you don't list them. Packages are declared with `PackageReference` items, right in the project file, and NuGet resolves the transitive dependencies at restore time. Reference assemblies for the target framework come from NuGet too, so you don't need a targeting pack installed.

Now, a detail about this repository that's worth being upfront about. Read the section of `legacy/README.md` called "How this differs from the real thing". The real FeeBilling solution uses old-style project files with `packages.config`, and it builds in Visual Studio. To make it build with the `dotnet` command line on any machine, this repo converted the legacy projects to SDK-style projects that still target `net472`. They compile against the `Microsoft.NETFramework.ReferenceAssemblies` package, so no targeting pack is needed.

That conversion needed two workarounds, and both teach you something about what breaks. First, the EDMX. Visual Studio normally splits the EDMX model into three embedded resources, the conceptual, storage and mapping models, using a build task called `EntityDeploy`. That task ships with Visual Studio, not with the Entity Framework package. So `legacy/FeeBilling.Data/FeeBilling.Data.csproj` has a custom target called `SplitEdmx` that uses `XmlPeek` to extract the three parts. Second, the T4 templates that generated the entity classes were removed, and the generated classes are checked in under the `Model` folder instead.

The interview point here is: converting to SDK-style `net472` makes the build modern. It doesn't move anything to .NET 10. Don't confuse the two.

And there's a trap in the conversion itself. Under `packages.config`, packages could run install scripts, called `install.ps1`, and apply content transforms, for example editing `Web.config` when installed. Under `PackageReference`, neither of those runs. So a package that used to add configuration on install silently stops doing it. Transitive dependencies also change from explicit to implicit, and binding redirects need to be regenerated. Visual Studio has a built-in migration command for this, but it doesn't support every project type, so check before you rely on it.

## Shared build infrastructure

Now let's look at how build settings are shared.

`Directory.Build.props` is a file MSBuild finds by walking up the folder tree from each project. The nearest one wins, and it doesn't automatically chain to the ones above it. FeeBilling has two. The root `Directory.Build.props` applies to everything under `src` and `tests`. It sets `LangVersion` to latest, turns on nullable reference types, sets `TreatWarningsAsErrors` to true, and sets `AnalysisLevel` to latest.

`legacy/Directory.Build.props` is the nearer file for legacy projects, and it deliberately doesn't import the root one. It sets `LangVersion` to 7.3, turns nullable off, sets `TreatWarningsAsErrors` to false and `AnalysisLevel` to none. It sets `AutoGenerateBindingRedirects` to true. And it has a `NoWarn` list that includes `NU1701`. Remember that one; we'll come back to it.

Then there's Central Package Management. At the root, `Directory.Packages.props` sets `ManagePackageVersionsCentrally` to true, and lists every package version once, as `PackageVersion` items, grouped with labels such as hosting, data, observability and testing. Individual projects then reference packages without a version. That's the answer to "how do you manage package versions across twenty projects": one file, one version per package, and every project agrees.

There's an optional extra called transitive pinning, enabled with the `CentralPackageTransitivePinningEnabled` property. It lets a central version also override a transitive dependency, which is how you force a patched version of something you don't reference directly.

Legacy opts out. `legacy/Directory.Packages.props` sets `ManagePackageVersionsCentrally` to false, with a comment saying legacy projects pin their own versions, as they did with `packages.config`. That's a reasonable choice for code you're trying not to change.

Notice one consequence of the modern settings. Because the modern code treats warnings as errors, a NuGet audit warning there breaks the build. In legacy, it's just noise in the log.

## Dependency triage

Now the core of the assessment: go through every legacy package and decide keep, upgrade, replace or remove, with a reason. The method is the same for each one. Ask four questions. Who uses it: which projects reference it, directly or transitively? Does it run on .NET 10, and does it run on Linux, since the target is containers? What's the decision? And why? The "why" column is the part that makes the table useful months later, when someone asks why a package was replaced instead of upgraded. Start the inventory with `dotnet list package` and the include-transitive option against the legacy solution, so you see the whole graph, not just what's written in the project files. Here's the FeeBilling list.

`EntityFramework` version 6.4.4 is used by almost everything: data, core, web, the billing runner, ingestion and the tests. Entity Framework 6.3 and later can run on modern .NET, but the EDMX tooling and designer story there is poor, so EF6 on modern .NET is a possible stepping stone, not the destination. The decision is to replace it with EF Core 10. That's lesson thirteen.

`log4net` version 2.0.8 is used by the web app, the core library, the billing runner, ingestion and invoicing. The restore flags it with a critical-severity advisory and a moderate one. The decision has two parts. In legacy, upgrade it now, because legacy is still production and will be for months. In modern code, replace it with `ILogger` of T and structured logging. That's lesson nineteen.

`Microsoft.AspNet.Mvc` and `Microsoft.AspNet.WebApi`, both version 5.2.7, are built on `System.Web`. There's no port. The decision is to rewrite the endpoints on ASP.NET Core, which is lesson seven.

`Newtonsoft.Json` version 11.0.2 runs fine on modern .NET, but it's flagged with a high-severity advisory. Bump it in legacy to a current 13.x version. In the modern APIs, use `System.Text.Json`, configured to keep the PascalCase names the front end expects. That's lesson eleven.

`Unity`, `Unity.Mvc` and `Unity.AspNet.WebApi`, version 5.11.1: the MVC and Web API integrations are `System.Web` only. Replace them with `Microsoft.Extensions.DependencyInjection`, which is lesson eight.

`PDFsharp` version 1.50.5147, used by invoicing, is the build that depends on GDI+ and `System.Drawing`, which is Windows-only in modern .NET. Evaluate cross-platform PDF options, including their licences. That's lesson twenty-one.

`Microsoft.AspNetCore.SystemWebAdapters.FrameworkServices` version 2.3.0 is the Framework side of the bridge the previous team added. It's transitional by design, and it gets removed at decommission.

And the MSTest packages, version 2.2.10, are old but irrelevant. Leave the legacy tests alone. The new parity tests will be xUnit in the modern solution.

Now that `NU1701` warning. It means a package was restored for a framework it doesn't target, so NuGet is guessing it will work. Legacy suppresses it. In an assessment, you want that signal back, because it tells you which packages are running somewhere they weren't built for.

## Security and licences

The restore is the first audit, whether you planned one or not. Run `dotnet restore` on `legacy/FeeBilling.sln` and you'll see NuGet audit warnings. They're numbered by severity: `NU1901` for low, `NU1902` for moderate, `NU1903` for high, and `NU1904` for critical. log4net 2.0.8 produces a `NU1904`.

Most vulnerable code arrives transitively, not directly. NuGet audit has a property called `NuGetAuditMode`: the value direct audits only your direct references, and the value all audits transitive ones too. The defaults have changed between SDK versions, so set it explicitly. Pair it with `NuGetAuditLevel` to choose the minimum severity that's reported. For ad-hoc checks, `dotnet list package` with the vulnerable and include-transitive options gives you a full report, as long as you have network access to nuget.org.

Run the same audit against `FeeBilling.Modern.slnx`, not just legacy. New code isn't automatically clean. It pulls in its own transitive graph, from Entity Framework Core, OpenTelemetry, Testcontainers and so on, and an advisory against any of those is your problem from day one. The outdated option of `dotnet list package` is useful too. It tells you how far behind each package is, which is a rough measure of upgrade effort.

Then decide the policy, and write it down. For the modern code, fail the build on high and critical findings, which the warnings-as-errors setting makes easy. For legacy, fix critical findings now, because it's still in production. "We're migrating anyway" is not a reason to leave a critical advisory in production for a year.

Licences are the other half, and in a regulated vendor they matter as much as vulnerabilities. According to the training plan, MediatR and AutoMapper moved to commercial licences in 2025. Check current terms before you rely on that, but the lesson holds either way: check the licence before you add a dependency, not after. If a package changes to a licence you can't accept, you pin the last acceptable version, or you replace it. A CQRS dispatcher, for example, is about fifty lines of your own code. PDF and imaging libraries are the next place to check, which comes up in lesson twenty-one.

## Inventory of Framework-only APIs

Packages are only half the story. The other half is which APIs the code uses that don't exist, or behave differently, in modern .NET. Build this inventory from the source with `git grep`, grouped by the lesson that fixes each group.

The web stack: `System.Web`, `HttpContext.Current` and `HttpRuntime.Cache`. Those are lessons seven, eight and ten.

Hosting: `ServiceBase` for the Windows Service, and `ServiceContract` and `OperationContract` for WCF. Lessons fifteen and seventeen.

Data: `TransactionScope`, the EDMX, `SqlDataAdapter` and `DataSet`. Lessons thirteen and sixteen.

Serialization and parsing: `BinaryFormatter`, `Encoding.Default`, and culture-dependent `decimal.Parse` and `DateTime.Parse`. Lesson eighteen.

Platform and configuration: `System.Drawing`, `ConfigurationManager` and log4net. Lessons nineteen and twenty-one.

Turn the grep output into counts per group. Those counts are your first size estimate for each work package, and they're far more defensible in a planning meeting than a guess.

## What tools do, and what they don't

Finally, the tooling question. Microsoft's .NET Upgrade Assistant was the tool for this kind of work, and Microsoft has been steering people toward GitHub Copilot's app modernization tooling. Check the current status and the recommended tool before you cite either one, because this area changes quickly.

Either way, here's what these tools do well. They convert project files to SDK-style. They change target frameworks. They update package references. And they apply some mechanical code fixes.

Here's what they don't do. They don't choose between CoreWCF and REST for your custodian feed. They don't replace MSDTC with a transactional outbox. They don't decide who owns the database schema. And they certainly don't prove that the new fee engine produces the same invoice to the cent. The one-line version is: tools do the project-file grunt work, and people do the architecture and the parity proof. In an interview, say clearly what the tool did and what you did.

## Traps

Here are the traps.

Treating the project conversion as the migration. SDK-style `net472` is still .NET Framework.

Losing `packages.config` behaviour silently. Install scripts and content transforms don't run under package references.

Hiding the signal. Suppressing `NU1701` hides exactly the packages you need to worry about.

Auditing only direct dependencies. Use transitive auditing.

Deferring legacy security fixes because you're migrating. Legacy is production until cutover.

Adding a package without checking its licence.

And expecting a tool to make architecture decisions.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do you assess a .NET Framework codebase before migrating it?

[pause 5s]

I start with a project and package inventory. Then I search the source for Framework-only API usage: `System.Web`, WCF services, `BinaryFormatter`, `System.Drawing`, `TransactionScope` with MSDTC, and `ConfigurationManager`, grouped by the work that will fix each one. Then I run a vulnerability and licence audit, including transitive dependencies. The output is a dependency triage table with a keep, upgrade, replace or remove decision per package, API usage counts that size each work package, an audit policy, and a risk register. Only then do I write a plan.

**Interviewer:** What does Upgrade Assistant, or Copilot app modernization, do and not do?

[pause 5s]

It automates project-file conversion, target framework changes, package updates and some mechanical code fixes. It doesn't choose the architecture: it won't decide between CoreWCF and REST, replace a distributed transaction with an outbox, or prove the new code behaves the same. Tools do the grunt work; people do the design and the parity proof. I'd also check which tool Microsoft currently recommends, because that's been changing.

**Interviewer:** How do you manage package versions across twenty projects?

[pause 5s]

Central Package Management. One `Directory.Packages.props` with `PackageVersion` entries, and projects reference packages without versions. Transitive pinning lets me force a patched version of a transitive dependency. Shared build settings, like nullable and warnings as errors, go in `Directory.Build.props`. In FeeBilling, the modern code uses both, and legacy deliberately opts out with its own props files.

**Interviewer:** A package you depend on changed to a commercial licence. What do you do?

[pause 5s]

First, I'd have caught it before adding the package, because in a regulated vendor licences are checked like vulnerabilities. If it happens anyway, I pin the last acceptable version while I evaluate a replacement, and replace it if the terms don't work. MediatR and AutoMapper are the well-known 2025 examples, and a small CQRS dispatcher is about fifty lines of our own code.

**Interviewer:** What's the risk in converting `packages.config` to package references?

[pause 5s]

Install scripts and content transforms don't run under package references, so packages that used to edit `Web.config` on install stop doing it, silently. Transitive dependencies go from explicit to implicit, so versions can shift. And binding redirects need regenerating. I'd diff the resolved package graph and the config files before and after, and I wouldn't treat the conversion itself as the migration.

## Recap

Five things to remember from this lesson.

One: an assessment produces artifacts: a dependency triage table, a Framework-only API inventory with counts, an audit policy, and a risk register.

Two: SDK-style `net472` makes the build modern but doesn't move you to .NET 10. And the conversion can silently drop install scripts and content transforms.

Three: `Directory.Build.props` and Central Package Management keep settings and versions consistent. The nearest props file wins, which is how legacy opts out.

Four: audit transitively, fix critical findings in legacy now, and check licences before adding packages.

Five: tools do the project-file grunt work; you do the architecture and prove parity.

In the next lesson, we'll build the most important safety net of the whole migration: a golden master that captures exactly what the legacy fee engine produces, so every later change can be proven against it.
