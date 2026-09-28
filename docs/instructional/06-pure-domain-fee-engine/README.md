# 06 · A Pure-Domain Fee Engine

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-02 · **Prerequisites:** 04, 05

**Audio lesson:** [06-pure-domain-fee-engine.mp3](06-pure-domain-fee-engine.mp3) · [Transcript](script.md)

## Why this video exists

The legacy fee logic is four static classes that reach into `HttpContext`, the ASP.NET cache, a stored procedure and log4net, and one of them is a copy-paste of another with the comment "Keep the two in sync!". It can't be unit tested, can't run outside IIS without a fake `HttpContext`, and can't run in a Linux container at all. Interviewers ask "How do you make untestable static business logic testable without changing its behaviour?" This video designs the replacement: a pure engine (inputs in, invoice lines and an audit trace out) whose behaviour is selected by an explicit `BillingPolicy`, and which the golden master from video 04 can prove equivalent to legacy.

## Learning objectives

By the end, the viewer can:

- Identify every hidden dependency in a static method: ambient context, cache, database, logging, clock.
- Design a pure engine: `FeeRequest` in, `FeeResult` out, no I/O, no clock.
- Use the strategy pattern for schedule types, and a policy object for the numeric behaviours that differ between legacy and corrected.
- Explain why strategies should return **tier lines**, not a single number, if legacy per-tier rounding has to be reproducible.
- Put the clock, cache, database and logging at the edges, and test time-dependent code with `FakeTimeProvider`.
- Keep a domain layer pure over time with architecture tests, and prove behaviour with example, property-based and parity tests.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do you make static, untestable business logic testable without changing behaviour? | Capture behaviour first (golden master). Then write a new pure implementation next to it, not a refactor of it. Make every hidden dependency an explicit input. Prove equivalence against the golden master with a legacy policy preset. Only then retire the old code. |
| Where does the clock go? | Not in the domain. The domain takes dates as inputs (period start/end, opened-on). The application edge asks a `TimeProvider` what "today" is, and tests replace it with `FakeTimeProvider`. And "local time" must name a time zone: in a container, local is usually UTC. |
| Strategy pattern or a switch statement? | Strategies when each variant has real behaviour and tests of its own (tiered, blended, flat, householded), selected by schedule type from DI. A switch is fine for a two-line mapping. The legacy smell here isn't the switch, it's the copy-pasted calculator class. |
| How do you keep a domain layer pure over time? | Make the wrong thing hard: no package references in the domain project, an architecture test that fails on forbidden dependencies, banned-API analyzers for `DateTime.Now`, and code review against an ADR that states the rule. |
| How do you make every calculation auditable? | Return a calculation trace with each result (tiers applied, AUM used, basis and factor, rounding mode and points, minimum applied, pro-rating) and store it with the invoice line. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Core/FeeCalculator.cs` | `DbContextFactory.Current`, `HttpRuntime.Cache`, `DateTime.Now`, `double`, `Log.Info` in one 40-line method |
| `legacy/FeeBilling.Core/BlendedFeeCalculator.cs` | "Copied from FeeCalculator for the blended schedules (FB-96, 2013). Keep the two in sync!" and the boundary condition |
| `legacy/FeeBilling.Core/FlatFeeCalculator.cs`, `HouseholdFeeService.cs` | Two more code paths with their own arithmetic |
| `legacy/FeeBilling.Data/DbContextFactory.cs` | "Anything that runs outside IIS ... has to fake an HttpContext first." |
| `legacy/FeeBilling.Core/AumService.cs` | AUM comes from `usp_GetBillableAum`: the database is inside the "calculation" |
| `legacy/FeeBilling.Core/QuarterHelper.cs` | `LastQuarterEnd()` reads `DateTime.Now` ("server local time") |
| `src/FeeBilling.Domain/` | Today: entities and value objects only (`BillingPeriod`, `AccountAum`, `AccountNumber`). The engine goes here. |
| `tests/FeeBilling.Domain.Tests/` | 12 tests, value objects only: where the engine's tests go |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | "Keep the two in sync!" Diff `FeeCalculator.cs` against `BlendedFeeCalculator.cs` on screen: two copies of the cache, the AUM lookup and the minimum-fee logic, with different rate logic in the middle. |
| 01:30–04:30 | Anatomy of a static calculator | List every dependency hidden in `CalculateQuarterlyFee`: `HttpContext.Current.Items` (via `DbContextFactory`), EF6, a stored procedure, `HttpRuntime.Cache` with a culture-dependent key and local-time expiry, `DateTime.Now`, log4net, and `double` money. Each one is a reason it can't be tested or containerized. |
| 04:30–08:30 | Target design | `FeeRequest` (schedule, billable AUM, period, opened-on, household members) → `FeeEngine` → `FeeResult` (amount, allocations, trace). Strategies per `FeeScheduleType`. `BillingPolicy` holds the four numeric dimensions from video 05. Key insight: legacy rounds **per tier after dividing by 4**, so a strategy that returns one annual number can't reproduce it. Strategies return tier lines; the engine applies basis and rounding per policy. |
| 08:30–11:00 | Edges vs core | Loading AUM, caching, logging and "what quarter is it?" belong to the application layer (API or worker). The engine doesn't need a clock at all. At the edge, `TimeProvider` replaces `DateTime.Now`; `FakeTimeProvider` fixes the "Depends on today's date" test. Moving to containers changes what "local time" means; name the time zone. |
| 11:00–13:00 | Minimum fees, households, boundaries | Account minimum applies to accounts with their own schedule; household minimum applies to the household fee *before* allocation, and members get no account minimum. Blended boundary: legacy uses `>` so an account at exactly $1,000,000 stays at 1.00%. A `>=` "cleanup" changes that fee by $625 a quarter. |
| 13:00–14:30 | The calculation trace | Every result carries the steps that produced it. Store it with the invoice line. It's what compliance asks for, what the parity report cites, and what the review screen drills into (video 22). |
| 14:30–17:30 | Proving it | Example tests for S1–S7 with the legacy policy. Property-based tests: allocation sums to the household fee; fee is non-decreasing in AUM; fee is never below the minimum. An architecture test that fails if Domain references EF Core, ASP.NET Core, `System.Web` or a logging framework. ≥95% branch coverage. Then the golden master: 0 differences with `BillingPolicy.Legacy`. |
| 17:30–19:00 | Where the engine runs | The worker (video 15) and the Billing API's what-if `PreviewFee` endpoint call it. Because Domain targets `netstandard2.0`, legacy *could* call it too, replacing the statics inside the IIS app. That's a valid option only with 0 parity differences, behind a flag, and if the worker migration is far off; otherwise it changes production legacy for no gain. |
| 19:00–20:00 | Recap | Pure core, explicit policy, dependencies at the edges, trace on every result, parity as the acceptance test. |

### Before: one method, seven dependencies

```csharp
public static decimal CalculateQuarterlyFee(int accountId, DateTime periodEnd)
{
    var db = DbContextFactory.Current; // pulled from HttpContext.Current.Items
    var account = db.Accounts
        .Include("FeeSchedule.Tiers")
        .Single(a => a.Id == accountId);

    var cacheKey = "aum_" + accountId + "_" + periodEnd.ToShortDateString(); // culture-dependent key
    var aum = HttpRuntime.Cache[cacheKey] as double?;
    if (aum == null)
    {
        aum = (double)AumService.GetBillableAum(accountId, periodEnd);
        HttpRuntime.Cache.Insert(cacheKey, aum, null,
            DateTime.Now.AddMinutes(30), Cache.NoSlidingExpiration); // local time
    }
```

The blended copy's boundary condition (`BlendedFeeCalculator.cs`):

```csharp
if (aum.Value > (double)tier.LowerBound || tier.LowerBound == 0)
{
    rate = (double)tier.AnnualRate;
}
```

### After: the shape of the engine (sketch, `FeeBilling.Domain`, `netstandard2.0`)

```csharp
public enum DayCountBasis { AnnualDividedByFour, Actual365 }
public enum RoundingPoint { LegacyPerTier, EndOfCalculation }
public enum AllocationMethod { LegacyProportional, LargestRemainder }

// Positional records need the IsExternalInit polyfill on netstandard2.0 (video 02).
// Legacy never constructs these, so the C# 7.3 consumer restriction doesn't apply.
public sealed record BillingPolicy(
    DayCountBasis Basis,
    RoundingPoint Rounding,
    MidpointRounding Midpoint,
    AllocationMethod Allocation,
    bool LegacyDoubleArithmetic)
{
    public static BillingPolicy Legacy { get; } = new(
        DayCountBasis.AnnualDividedByFour, RoundingPoint.LegacyPerTier, MidpointRounding.ToEven,
        AllocationMethod.LegacyProportional, LegacyDoubleArithmetic: true);

    public static BillingPolicy Corrected { get; } = new(
        DayCountBasis.Actual365, RoundingPoint.EndOfCalculation, MidpointRounding.AwayFromZero,  // midpoint: per fee agreement
        AllocationMethod.LargestRemainder, LegacyDoubleArithmetic: false);
}

public interface IFeeStrategy
{
    FeeScheduleType Handles { get; }

    // Tier lines, not a single number, so the engine can round per tier when the policy says so.
    AnnualFeeResult CalculateAnnual(Money billableAum, FeeSchedule schedule);
}

public sealed class FeeEngine(IEnumerable<IFeeStrategy> strategies, BillingPolicy policy)
{
    private readonly Dictionary<FeeScheduleType, IFeeStrategy> _strategies =
        strategies.ToDictionary(s => s.Handles);

    // Pure: no I/O, no clock, no logging. Same inputs, same output, every time.
    public FeeResult Calculate(FeeRequest request)
    {
        var annual = _strategies[request.Schedule.ScheduleType].CalculateAnnual(request.BillableAum, request.Schedule);
        var trace = new CalculationTrace(annual.Tiers, policy, request.Period);
        // Apply basis and rounding per policy, then the minimum, then pro-rating; record each step in the trace.
        // ...
        return new FeeResult(/* amount */ default, trace);
    }
}
```

`LegacyDoubleArithmetic` only applies to the paths legacy computed in `double` (`FeeCalculator`, `BlendedFeeCalculator`). `HouseholdFeeService` was `decimal`. "Legacy" isn't one behaviour; it's the behaviour of each legacy code path, and the policy has to model that.

### The clock at the edge (sketch, application layer, `net10.0`)

```csharp
public sealed class CurrentPeriodResolver(TimeProvider clock, TimeZoneInfo firmTimeZone)
{
    // Replaces QuarterHelper.LastQuarterEnd(), which used DateTime.Now in the server's local time.
    public DateTime LastQuarterEnd()
    {
        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), firmTimeZone).Date;
        var quarter = BillingPeriod.ForQuarterEnding(today);
        return today == quarter.End ? today : quarter.Start.AddDays(-1);
    }
}

// Test: new FakeTimeProvider(new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero))
// (Microsoft.Extensions.TimeProvider.Testing). In UTC that's September 30, so the last quarter end
// is 2026-09-30. In Toronto it's still 9 p.m. on September 29, so the answer is 2026-06-30.
```

### Keeping Domain pure (sketch, `tests/FeeBilling.Domain.Tests`)

```csharp
[Fact]
public void Domain_has_no_infrastructure_dependencies()
{
    var result = Types.InAssembly(typeof(BillingPeriod).Assembly)
        .ShouldNot()
        .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "System.Web",
                             "log4net", "Microsoft.Extensions.Logging")
        .GetResult();

    Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
}
```

This uses NetArchTest (ArchUnitNET is an alternative); check the package's maintenance status before adopting it. A simpler guard also works: fail CI if `FeeBilling.Domain.csproj` gains any `PackageReference`.

## Demo

```bash
# The duplication, side by side
git diff --no-index legacy/FeeBilling.Core/FeeCalculator.cs legacy/FeeBilling.Core/BlendedFeeCalculator.cs

# Every hidden dependency in the fee path
git grep -nE "DbContextFactory\.Current|HttpRuntime\.Cache|DateTime\.Now|LogManager|\(double\)" -- legacy/FeeBilling.Core

# Today's Domain tests: value objects only
dotnet test tests/FeeBilling.Domain.Tests

# Coverage for the engine once it exists (add coverlet.collector to Directory.Packages.props first)
dotnet test tests/FeeBilling.Domain.Tests --collect:"XPlat Code Coverage"
```

Example-based tests read expected values as invariant-culture strings, because `decimal` can't be an attribute argument:

```csharp
[Theory]
[InlineData("2500000.00", "5312.50")]   // S1 mid-tier
[InlineData("1000000.00", "2500.00")]   // S2 exactly on the boundary
[InlineData("80000.00",   "250.00")]    // S3 minimum fee
public void Legacy_policy_tiered_matches_golden_scenarios(string aum, string expected)
{
    var result = Engine(BillingPolicy.Legacy).Calculate(TieredRequest(Parse(aum)));
    Assert.Equal(Parse(expected), result.Amount);
}

private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
```

## Traps to call out

- **Refactoring the static classes in place.** Build the new engine beside them. The legacy code is the measuring stick until cutover.
- **Strategies that return a single annual number.** Then `BillingPolicy.Legacy` can't reproduce per-tier rounding, and you'll never reach 0 parity differences.
- **Putting the cache in the domain.** Caching AUM is an application concern. Legacy's cache is also a correctness bug: a 30-minute, process-wide cache means a recalculation can use stale AUM.
- **Injecting `TimeProvider` into the engine "for flexibility".** If the engine needs today's date, the inputs are incomplete. Dates are request data.
- **"Local time" in a container.** Legacy's `DateTime.Now` meant the on-prem server's time zone. In a container it's usually UTC. Name the firm's time zone explicitly.
- **Fixing the blended boundary (`>` vs `>=`) while porting.** It might be a bug. It's also what clients were billed. Capture it, raise it, flag it.
- **Minimum fee at the wrong level.** Household minimum applies before allocation; members don't get an account minimum on top.
- **Zero-AUM households.** Legacy throws. Decide the corrected behaviour with the business, record it in an ADR, and test it.

## Key terms

Pure function · hidden dependency · ambient context · strategy pattern · policy object · calculation trace · functional core, imperative shell · `TimeProvider` / `FakeTimeProvider` · property-based testing · architecture test · branch coverage

## After the video

1. Implement `IFeeStrategy` for tiered, blended, flat and householded schedules, with `BillingPolicy.Legacy`, and get S1–S7 green.
2. Add a blended account at exactly $1,000,000 to your test data and confirm the legacy engine charges 1.00%, not 0.75%.
3. Write the three property-based tests with CsCheck or FsCheck. Make sure the generators produce zero AUM, single-member households and tier-boundary values.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 8 (architectural principles 1, 2 and 6) and Section 9 (WP-02)
- Microsoft Learn: *TimeProvider* and *FakeTimeProvider* (`Microsoft.Extensions.TimeProvider.Testing`); `Microsoft.Bcl.TimeProvider` for `netstandard2.0`
- Gary Bernhardt, *Functional Core, Imperative Shell*
- NetArchTest, ArchUnitNET; CsCheck, FsCheck (check current versions and maintenance status)
