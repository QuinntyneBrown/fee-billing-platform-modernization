# 02 · Target Frameworks and Shared Libraries

Welcome back. This lesson covers two questions that come up early in every modernization interview. Which version of .NET would you target? And how do the old code and the new code share anything while both are running? FeeBilling has a live example of each. The new services already target .NET 10, but the decision record still says .NET 8. And one library, `FeeBilling.Domain`, is compiled once and used by both the .NET Framework 4.7.2 code and the .NET 10 services. By the end of this lesson, you'll be able to justify the target version, fix a stale decision properly, and explain exactly what a `netstandard2.0` library does and doesn't give you as a bridge.

## The questions this lesson answers

Here's what you should be able to answer afterwards. Why .NET 10 and not .NET 8? How do legacy and new code share types during a migration? What's the difference between .NET Standard and .NET? Your shared library needs a date-only type, or init-only setters, or records: what now? And what would you do in your first week about the target version?

Keep one idea in mind: a shared library during a migration has two separate constraints. One is the language version it's compiled with. The other is the set of APIs it's compiled against. People mix those up, and the difference is exactly what an interviewer is probing for.

## The support lifecycle

Start with how .NET is released. Since .NET 5, Microsoft ships a new major version every November. Even-numbered releases are long-term support, or LTS, and get three years of patches. Odd-numbered releases are standard-term support, or STS. The training plan notes that STS support was extended to twenty-four months, which puts .NET 9's end date alongside .NET 8's. Treat all of these dates as "as of September 2026", and check Microsoft's support policy page before you quote them in an interview.

Here are the dates that matter for FeeBilling. .NET 8 is LTS, and its support ends on November 10, 2026. That's about two months from now. .NET 10 is LTS, released in November 2025, and supported until November 2028. .NET Framework is a different kind of product. Versions 4.7.2, 4.8 and 4.8.1 are Windows components, and their support follows the Windows lifecycle. They aren't dying, but they're frozen: no new features, Windows only, and 4.8.1 is the last version there will ever be.

So why .NET 10 and not .NET 8? If you start a migration on .NET 8 today, you'll have to upgrade again within months, while you're still in the middle of the first migration. That's wasted effort and wasted risk.

There's a useful follow-up point. If part of an estate is already on .NET 8, moving it to .NET 10 is cheap compared to moving .NET Framework to .NET 10. The 8 to 10 upgrade is mostly a target framework change and package bumps. The Framework migration is the real project. So do the cheap upgrade first. It's a quick win, and it builds the upgrade pipeline everyone else will follow. And budget for an LTS-to-LTS upgrade every two years from now on. The goal is to make it routine, not a project.

## When the code and the ADR disagree

Now let's look at FeeBilling. Open `docs/adr/0002-target-dotnet-version.md`. Its decision section says new services target .NET 8, the long-term support release. Its table even lists .NET 9's end of support as May 2026, which is another fact that has since moved.

Now run a `git grep` for the TargetFramework property across the `src`, `tests` and `tools` folders. Every modern project already says `net10.0`. `src/FeeBilling.Accounts.Api/FeeBilling.Accounts.Api.csproj` says `net10.0`. So does the gateway, the infrastructure project, both test projects and the database init tool. The code moved on, and the decision record didn't.

That's handover issue number one, and the interview lesson isn't really about which version is right. It's that the code and the record must agree, and when they don't, you fix whichever one is wrong and write down why. In your first week, check what the code actually targets, not what the ADR says.

The way to fix an accepted ADR is to supersede it, not to edit it. Write a new ADR with the next free number. It says: new services target .NET 10, shared libraries stay on `netstandard2.0` until the last Framework consumer is retired, and we plan an LTS upgrade every two years. Then change the status line of ADR-0002 to say it's superseded by the new one. Don't rewrite its decision. The old ADR records why .NET 8 was a reasonable choice in March 2025, and that history is worth keeping. Anyone reading the log later can follow the chain.

## .NET Standard as a bridge

Now the second problem: sharing code while both stacks are running.

First, a definition you should be able to give in one breath. .NET Standard is not a runtime. It's a contract: a versioned list of APIs that implementations promise to provide. .NET Framework, modern .NET, Mono and Xamarin are implementations. A library that targets `netstandard2.0` can run on any implementation that supports version 2.0 of the contract.

Why version 2.0 specifically? Because it's the highest version .NET Framework implements. .NET Standard 2.1 exists, but .NET Framework never implemented it. And .NET Standard itself is frozen: there won't be a 2.2. New code simply targets `net10.0`. So `netstandard2.0` survives for one reason: it's the bridge to .NET Framework.

There's a version detail worth knowing. .NET Framework 4.6.1 through 4.7.1 claimed support for .NET Standard 2.0, but they did it through shim assemblies that caused a lot of binding-redirect pain. From 4.7.2 onwards, the support is built in. FeeBilling runs on 4.7.2, so it's on the right side of that line.

Here's how FeeBilling uses the bridge. Open `src/FeeBilling.Domain/FeeBilling.Domain.csproj`. It has a single TargetFramework of `netstandard2.0`, and a comment that says it's shared between the legacy .NET Framework solution and the new .NET 10 services during the transition, and should be retargeted to net10.0 once no Framework project references it. That comment is the exit plan, written into the project file.

Now look at the two solution files. `legacy/FeeBilling.sln` includes `FeeBilling.Domain`, reaching over into the `src` folder. `FeeBilling.Modern.slnx` includes it too. And `legacy/FeeBilling.Core/FeeBilling.Core.csproj` has a project reference that climbs two folders up and into `src`, to the Domain project. One source, two consumers.

There's a bonus to `netstandard2.0` that's easy to miss: it's a guardrail. `System.Web` isn't part of it. So the shared library physically can't reach for `HttpContext.Current`, the way `FeeBilling.Core` does. If you targeted the shared library at `net472` instead, "because legacy needs it", you'd lose that protection, and someone would eventually add a dependency on `System.Web` or `ConfigurationManager`. Watch out for compatibility packages too, such as the one that brings `ConfigurationManager` to modern targets. They quietly reopen the same door.

## Language version versus API surface

This is the part interviewers use to separate people who've done it from people who've read about it.

Consumers of a library see compiled IL, not C# syntax. That means the language version the library is compiled with mostly doesn't matter to its consumers. What matters is the API surface: the types and members it exposes and uses.

Look at the build settings. `legacy/Directory.Build.props` sets `LangVersion` to 7.3, which is what Visual Studio 2017-era Framework projects compiled with, and it deliberately doesn't import the root build props. The root `Directory.Build.props` sets `LangVersion` to latest and turns on nullable reference types. `FeeBilling.Domain` lives under `src`, so it picks up the root file and compiles with the latest C#.

Now open `src/FeeBilling.Domain/ValueObjects/AccountAum.cs`. Its summary comment says: legacy HouseholdAllocator consumes this type, so it has to stay C# 7.3-friendly. And yet the file uses a file-scoped namespace, which is C# 10, and the pattern `is not null`, which is C# 9. Is that a contradiction? No. Those are pure compiler features. They compile to ordinary IL, and the C# 7.3 consumer never sees them. What the comment really means is that the type's public shape must be something a C# 7.3 project can use.

Nullable annotations are similar. The compiler emits attributes, and a C# 7.3 consumer simply ignores them. So don't rely on nullable annotations to protect legacy callers from passing nulls. The legacy code won't get a warning.

So what does leak through? Things that need runtime or library support. Let's go through them.

Init-only setters and records compile on `netstandard2.0` only if you add a tiny polyfill: an internal static class called `IsExternalInit` in the `System.Runtime.CompilerServices` namespace. Even then, a C# 7.3 project can't call an init setter. So avoid init setters on any type that legacy constructs.

Required members need polyfills too, and older compilers are blocked from using constructors of types that have them. Avoid them on shared types.

`DateOnly` and `TimeOnly` don't exist in `netstandard2.0` at all. The workaround is to use `DateTime` with a date-only convention. `BillingPeriod` does exactly that: its constructor keeps only the date part of each value, using `.Date`.

`TimeProvider` isn't in the `netstandard2.0` base library. There's a package, `Microsoft.Bcl.TimeProvider`, that adds it. But the better answer, which lesson six covers, is to keep the clock out of the domain entirely.

Default interface members need runtime support that .NET Framework doesn't have, so use an abstract base class or extension methods instead.

And `Span` of T and related types come in through packages such as `System.Memory`. Which brings up a subtle cost. Every package the Domain library references also becomes a dependency of the legacy IIS application. And binding redirects in that application are a classic source of runtime `FileLoadException` errors. So every package added to Domain has to be justified.

## Multi-targeting and pinning the SDK

Sometimes the modern services need an API that `netstandard2.0` doesn't have. The answer is multi-targeting. Instead of a single TargetFramework, the project uses the plural `TargetFrameworks` property, listing `netstandard2.0` and `net10.0` separated by a semicolon. The build then produces two assemblies. Modern consumers get the `net10.0` build, and legacy gets the `netstandard2.0` build.

Inside the code, you use a conditional compilation block, one that checks whether this build is for modern .NET, to pick a different implementation per target. For example, a guard method might call `ArgumentNullException.ThrowIfNull` on .NET 10 and throw the exception by hand on `netstandard2.0`.

The rule is: use conditional compilation for implementation differences only. Keep the public API identical across targets, and keep the behaviour identical too. Here's a behavioural trap. Say you normalize a date on the .NET 10 side by converting it to a `DateOnly` and back to a `DateTime`. The result has a DateTimeKind of Unspecified. On the `netstandard2.0` side, taking `.Date` of the value keeps whatever kind the input had. Same method, same signature, different behaviour. That's exactly how you end up with the date-off-by-one bug covered in lesson eleven. During a transition, legacy and modern code must mean the same thing by the same type.

When do you drop `netstandard2.0`? When the last `net472` consumer is retired. At that point you retarget Domain to `net10.0`, delete the polyfills, and use the modern APIs freely. Put that step on the decommission checklist, so the transition tool doesn't become permanent.

One more piece of build hygiene: pin the SDK. Open `global.json` at the repository root. It specifies SDK version 10.0.401, a `rollForward` policy of `latestFeature`, and no prereleases. The roll-forward policy means a machine can use a newer feature band or patch of the 10.0 SDK, but never .NET 11. So local builds and CI builds use the same major SDK. And the CI workflow, `.github/workflows/ci.yml`, uses the same file: both the legacy job and the modern job pass `global.json` to the setup step. One file controls the SDK everywhere.

## Traps

Here are the traps to name.

Trusting the ADR over the code, or the code over the ADR. They must agree. Fix whichever is wrong, and record why.

Editing an accepted ADR's decision in place. You lose the history of why .NET 8 was chosen. Supersede it instead.

Targeting the shared library at `net472` because legacy needs it. You lose the `netstandard2.0` guardrail, and nothing stops someone reaching for `System.Web`.

Adding packages to Domain casually. Each one ships into the IIS app, with binding-redirect risk.

Letting the two targets of a multi-targeted library diverge, in public API or in behaviour.

And keeping `netstandard2.0` forever. It's a transition tool with an exit date.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** Why .NET 10 and not .NET 8?

[pause 5s]

As of September 2026, .NET 8 support ends on November 10, 2026, and .NET 10 is the current long-term support release, supported until November 2028. Starting a migration on .NET 8 now means a second upgrade within months, in the middle of the first one. If some of the estate is already on .NET 8, I'd move it to .NET 10 first. It's cheap compared with the Framework migration, and it sets up the upgrade pipeline everyone else will use.

**Interviewer:** How do legacy and new code share types during a migration?

[pause 5s]

I'd extract the shared types into a library that targets `netstandard2.0`, which both .NET Framework 4.7.2 and later and .NET 10 can reference. I'd keep it free of `System.Web`, Entity Framework and logging, and `netstandard2.0` helps enforce that because `System.Web` isn't in it. In FeeBilling that's `FeeBilling.Domain`, which appears in both solutions. Once the last Framework consumer is retired, I'd retarget it to `net10.0`.

**Interviewer:** What's the difference between .NET Standard and .NET?

[pause 5s]

.NET Standard is a versioned contract: a set of APIs that implementations promise to provide. .NET Framework, modern .NET and Mono are implementations. .NET Standard is frozen at 2.1, and .NET Framework only implements 2.0. So new code targets `net10.0` directly, and `netstandard2.0` survives only as the bridge to .NET Framework.

**Interviewer:** Your shared library needs `DateOnly`, init setters or records. What now?

[pause 5s]

`DateOnly` isn't available on `netstandard2.0`, so I'd use `DateTime` with a date-only convention, the way `BillingPeriod` does. Init setters and records compile with an `IsExternalInit` polyfill, but a legacy project compiling at C# 7.3 can't call an init setter, so I'd avoid them on types legacy constructs. If the modern side genuinely needs newer APIs, I'd multi-target `netstandard2.0` and `net10.0`, keep the public API and behaviour identical, and put the differences behind conditional compilation.

**Interviewer:** What would you do in your first week about the target version?

[pause 5s]

Check what the code actually targets, not what the ADR says. In FeeBilling, every modern project targets `net10.0`, but ADR-0002 still says .NET 8. I'd write a new ADR that supersedes it, recording .NET 10 for services, `netstandard2.0` for shared libraries until the Framework consumers are gone, and the reason: .NET 8's end-of-support date. I'd also confirm `global.json` pins the SDK for both local builds and CI.

## Recap

Five things to remember from this lesson.

One: target .NET 10. As of September 2026, .NET 8 ends support on November 10, 2026, and .NET 10 is supported until November 2028. Budget for an LTS upgrade every two years.

Two: when the code and the ADR disagree, fix the one that's wrong and supersede the ADR rather than editing it.

Three: .NET Standard is a contract, not a runtime. `netstandard2.0` is the bridge to .NET Framework, and it doubles as a guardrail against `System.Web`.

Four: language version and API surface are different constraints. Compiler features like file-scoped namespaces are free; APIs and runtime features like init setters, `DateOnly` and default interface members are not.

Five: multi-target only with identical public API and behaviour, pin the SDK with `global.json`, and plan the day you drop `netstandard2.0`.

In the next lesson, we'll assess the project system and the dependencies: how the projects build, which packages they pull in, and which of those are vulnerable, Framework-only, or carry licence terms you can't accept.
