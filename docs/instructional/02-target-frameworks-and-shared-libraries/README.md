# 02 · Target Frameworks and Shared Libraries

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work packages:** prerequisite for all; fixes handover issue #1 · **Prerequisites:** 01

**Audio lesson:** [02-target-frameworks-and-shared-libraries.mp3](02-target-frameworks-and-shared-libraries.mp3) · [Transcript](script.md)

## Why this video exists

Two questions come up early in every modernization interview: "Which .NET version would you target?" and "How do the old and new code share anything while both are running?" FeeBilling shows both problems. The services in `src/` already target `net10.0`, but ADR-0002 still says .NET 8. And `src/FeeBilling.Domain` is compiled once and consumed by both the .NET Framework 4.7.2 `FeeBilling.Core` and the .NET 10 services. This video explains the support lifecycle, the right way to fix a stale ADR, and what `netstandard2.0` does and doesn't give you as a bridge.

## Learning objectives

By the end, the viewer can:

- Explain the difference between LTS and STS releases, and justify .NET 10 as the target for a migration starting in late 2026.
- Supersede an out-of-date ADR without rewriting history.
- Explain what .NET Standard is (an API contract) versus .NET (an implementation), and why `netstandard2.0` is the only version .NET Framework can consume.
- Separate the two constraints on a shared library: the **language version** it's compiled with, and the **API surface** it's compiled against.
- Choose between a single `netstandard2.0` target and multi-targeting (`netstandard2.0;net10.0`), and say when to drop `netstandard2.0` entirely.
- Pin the SDK with `global.json` and explain `rollForward`.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| Why .NET 10 and not .NET 8? | .NET 8 support ends November 10, 2026. .NET 10 is LTS until November 2028. Starting a migration on .NET 8 now means a second upgrade within months. If some of the estate is already on 8, move it to 10 first as a cheap win that establishes the upgrade pipeline. |
| How do legacy and new code share types during a migration? | Extract the shared types into a library targeting `netstandard2.0`, which both .NET Framework 4.7.2+ and .NET 10 can reference. Keep it free of `System.Web`, EF and logging. Retarget it to `net10.0` once the last Framework consumer is retired. |
| What's the difference between .NET Standard and .NET? | .NET Standard is a versioned set of APIs that implementations promise to provide. .NET Framework, .NET (Core), Mono and Xamarin are implementations. .NET Standard is frozen at 2.1; new code targets `net10.0` directly. `netstandard2.0` survives only because it's the bridge to .NET Framework. |
| Your shared library needs `DateOnly` / `init` / records. What now? | `DateOnly` isn't in `netstandard2.0`. `init` and records compile with a small `IsExternalInit` polyfill, but a legacy project compiling at C# 7.3 can't use `init` setters. Either multi-target and hide the difference behind `#if NET`, or keep the shared API to what both sides can use. |
| What would you do in the first week about the target version? | Check what the code actually targets, not what the ADR says. Here they disagree. Supersede the ADR so the record matches reality and explains why. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `docs/adr/0002-target-dotnet-version.md` | "New services target **.NET 8 (LTS)**" and the table that lists .NET 9 support ending May 2026 |
| `src/FeeBilling.Accounts.Api/FeeBilling.Accounts.Api.csproj` | `<TargetFramework>net10.0</TargetFramework>`: the code already disagrees with the ADR |
| `src/FeeBilling.Domain/FeeBilling.Domain.csproj` | `netstandard2.0` and the comment: "Retarget to net10.0 once no Framework project references it" |
| `legacy/FeeBilling.sln`, `FeeBilling.Modern.slnx` | `FeeBilling.Domain` appears in **both** solutions |
| `legacy/FeeBilling.Core/FeeBilling.Core.csproj` | `ProjectReference` to `..\..\src\FeeBilling.Domain\FeeBilling.Domain.csproj` |
| `legacy/Directory.Build.props` vs `Directory.Build.props` | Legacy compiles at `LangVersion 7.3` and doesn't import the root file; the root file sets `LangVersion latest` and `Nullable enable` |
| `src/FeeBilling.Domain/ValueObjects/AccountAum.cs` | "Legacy HouseholdAllocator consumes this type, so it has to stay C# 7.3-friendly", yet it uses a file-scoped namespace and `is not null` |
| `global.json` | SDK `10.0.401`, `rollForward: latestFeature`, no prereleases |
| `.github/workflows/ci.yml` | Both jobs use `global-json-file: global.json` |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | The csproj says `net10.0`. ADR-0002 says .NET 8. Which one is right, and why does the disagreement matter more than either answer? |
| 01:30–05:00 | Support lifecycle | LTS vs STS. Walk the table below. .NET 8 → 10 is cheap; .NET Framework → 10 is the real migration. Budget for an LTS-to-LTS upgrade every two years; make it routine, not a project. |
| 05:00–07:30 | Superseding ADR-0002 | Don't edit an accepted ADR's decision in place. Write a new ADR (next free number), mark ADR-0002 `Superseded by ADR-00NN`, and record *why*: the .NET 8 end-of-support date, and that the code already moved. |
| 07:30–11:00 | .NET Standard as a bridge | .NET Standard is a contract, not a runtime. `netstandard2.0` is the highest version .NET Framework implements. Use 4.7.2 or later: 4.6.1–4.7.1 claimed 2.0 support through shim assemblies and caused binding-redirect pain. `netstandard2.0` is also a **guardrail**: `System.Web` isn't in it, so the shared library can't quietly depend on `HttpContext`. |
| 11:00–14:30 | Language version vs API surface | Consumers see IL, not C# syntax. Domain compiles with `LangVersion latest` (root props), so file-scoped namespaces and `is not null` are fine even though `FeeBilling.Core` compiles at 7.3. Nullable annotations are simply ignored by the 7.3 consumer. What *does* leak: APIs and features that need runtime or BCL support (table below). Every package Domain adds also becomes a dependency of the IIS app, with binding-redirect risk. |
| 14:30–17:30 | Multi-targeting | `<TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>` gives modern consumers a `net10.0` build and legacy the `netstandard2.0` one. Use `#if NET` for implementation differences only; keep the public API identical across targets, or legacy and modern stop meaning the same thing by the same type. |
| 17:30–19:00 | Pinning the SDK | `global.json` makes local builds and CI use the same SDK. `latestFeature` allows newer 10.0 feature bands and patches, never .NET 11. CI reads the same file. |
| 19:00–20:00 | Recap | Target .NET 10 now; share through `netstandard2.0`; have an exit plan: when the last `net472` consumer is retired, retarget Domain to `net10.0` and delete the polyfills. |

### Support lifecycle (as of September 2026; verify before recording)

| Version | Type | End of support | Note |
|---|---|---|---|
| .NET Framework 4.7.2 / 4.8 / 4.8.1 | Windows component | Follows the Windows lifecycle | Frozen: no new features, Windows only. 4.8.x is the last version. |
| .NET 8 | LTS | November 10, 2026 | ADR-0002's choice. Two months left. |
| .NET 9 | STS | November 10, 2026 (per the training plan, after STS support was extended to 24 months) | ADR-0002 says May 2026: another stale fact |
| .NET 10 | LTS | November 2028 | The target |

### What `netstandard2.0` does and doesn't give you

| Want to use in Domain | On `netstandard2.0`? | Options |
|---|---|---|
| File-scoped namespaces, pattern matching, `is not null`, switch expressions | Yes | Pure compiler features. Compile the library with a modern `LangVersion`. |
| Nullable reference types | Yes (compiler emits the attributes) | A C# 7.3 consumer ignores the annotations. Don't rely on them to protect legacy callers. |
| `init` setters, records | Only with an `IsExternalInit` polyfill | And a C# 7.3 consumer can't call an `init` setter. Avoid them on types legacy constructs. |
| `required` members | Needs polyfills, and older compilers are blocked from using those constructors | Avoid on shared types (verify exact behaviour before recording) |
| `DateOnly`, `TimeOnly` | No | Use `DateTime` with a "date only" convention, as `BillingPeriod` does. |
| `TimeProvider` | Not in the BCL | `Microsoft.Bcl.TimeProvider` package. Better: keep the clock out of Domain entirely (video 06). |
| Default interface members | No (needs runtime support .NET Framework lacks) | Abstract base class or extension methods |
| `Span<T>`, `System.Memory` | Via packages | Each package flows into the IIS app. Justify it. |

### Before and after

The shared library as found (`src/FeeBilling.Domain/FeeBilling.Domain.csproj`):

```xml
<PropertyGroup>
  <TargetFramework>netstandard2.0</TargetFramework>
  <RootNamespace>FeeBilling.Domain</RootNamespace>
</PropertyGroup>
```

A multi-targeted sketch, if modern consumers need APIs `netstandard2.0` lacks:

```xml
<PropertyGroup>
  <TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
  <RootNamespace>FeeBilling.Domain</RootNamespace>
</PropertyGroup>
```

```csharp
public static class Guard
{
    // Same public signature and the same observable behaviour on both targets.
    public static void NotNull(object? value, string paramName)
    {
#if NET
        ArgumentNullException.ThrowIfNull(value, paramName);
#else
        if (value is null) throw new ArgumentNullException(paramName);
#endif
    }
}
```

Behaviour must match, not just signatures. A tempting `#if NET` branch that normalizes dates with `DateOnly.FromDateTime(value).ToDateTime(TimeOnly.MinValue)` returns `Kind=Unspecified`, while the `value.Date` branch keeps the input's `Kind`. That's the kind of divergence that turns into a date-off-by-one bug (see video 11).

## Demo

```bash
# What the code actually targets, versus what ADR-0002 says
git grep -n "<TargetFramework" -- src tests tools legacy
grep -n "Domain" legacy/FeeBilling.sln FeeBilling.Modern.slnx

# The SDK both CI jobs use
cat global.json
dotnet --list-sdks

# Domain is built by both solutions, from the same source, with the root Directory.Build.props
dotnet build legacy/FeeBilling.sln
dotnet build FeeBilling.Modern.slnx
```

Live experiment (revert afterwards):

1. Add `public decimal Rounded { get; init; }` to `AccountAum` and build `src/FeeBilling.Domain`. It fails because `IsExternalInit` isn't defined for `netstandard2.0`.
2. Add the polyfill (`namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }`). Domain builds.
3. Try to set the property from `legacy/FeeBilling.Core`. The C# 7.3 project can't use it. The language version of the *consumer* now matters.

## Traps to call out

- **Trusting the ADR over the code (or the code over the ADR).** They must agree. Fix whichever is wrong, and record why.
- **Editing an accepted ADR's decision in place.** You lose the history of why .NET 8 was chosen in 2025. Supersede it instead.
- **Targeting `net472` for the shared library "because legacy needs it".** You lose the `netstandard2.0` guardrail: nothing stops someone from reaching for `System.Web` or `ConfigurationManager`. Watch for compatibility packages (such as `System.Configuration.ConfigurationManager`) that quietly re-open that door.
- **Adding packages to Domain casually.** Each one ships into the IIS app, and binding redirects there are a classic source of runtime `FileLoadException`s.
- **Letting the two targets of a multi-targeted library diverge in public API.** Legacy and modern code must mean the same thing by the same type during the transition.
- **Keeping `netstandard2.0` forever.** It's a transition tool. Put "retarget Domain to `net10.0`" on the decommission checklist (video 23).

## Key terms

LTS / STS · end of support · .NET Standard · target framework moniker (TFM) · multi-targeting · `#if NET` · polyfill (`IsExternalInit`) · `LangVersion` · binding redirect · `global.json` / `rollForward` · superseded ADR

## After the video

1. Write the ADR that supersedes ADR-0002: .NET 10 for services, `netstandard2.0` for shared libraries until the last Framework consumer is retired, and the planned cadence for the next LTS upgrade.
2. List every type in `src/FeeBilling.Domain` that `legacy/` references (`git grep -n "FeeBilling.Domain" -- legacy`). That list is the API you can't change freely until cutover.
3. Explain out loud, in under a minute, why `AccountAum` can use `is not null` but must not use an `init` setter.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 2 (target platform decisions) and Section 12.2 ("Why .NET 10 and not .NET 8?")
- `docs/adr/0002-target-dotnet-version.md`, `docs/handover.md` (known issue #1)
- Microsoft Learn: *.NET Standard*, *Target frameworks in SDK-style projects*, *Cross-platform targeting for .NET libraries*
- Microsoft: *.NET and .NET Core support policy* (check current dates)
- Microsoft Learn: *global.json overview* (`rollForward` values)
