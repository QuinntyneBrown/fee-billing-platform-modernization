# 05 · Money, Rounding and Numeric Parity

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work packages:** WP-01, WP-02 · **Prerequisites:** 04

## Why this video exists

Fee billing is arithmetic that clients and regulators audit. The FeeBilling fee engine does that arithmetic in `double`, rounds at every tier, divides by 4 regardless of the fee agreement, and allocates household fees in a way that can create or lose a cent. Every one of those is a trap in a migration: "clean it up" and invoices change. This video shows each numeric behaviour in the legacy code, proves it with real numbers, and gives the interview answer to "You find a bug in the legacy fee calculation. What do you do?"

## Learning objectives

By the end, the viewer can:

- Explain why `double` is wrong for money and `decimal` is right, with a concrete FeeBilling example that differs by a cent.
- Explain `MidpointRounding.ToEven` (banker's rounding) vs `AwayFromZero`, and why every `Math.Round` in money code must pass the mode explicitly.
- Distinguish per-tier rounding from end-of-calculation rounding, and quantify the effect.
- Explain day-count basis (annual ÷ 4 vs actual/365) as a business decision the code must make configurable.
- Implement largest-remainder allocation so a household's allocations always sum to its fee.
- Reproduce legacy numeric behaviour on purpose, and ship corrections separately behind per-firm flags.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| Why `decimal` for money? | `double` is binary floating point: most decimal fractions (0.1, 0.0075) have no exact representation, so arithmetic drifts and midpoints land on the wrong side. `decimal` is base-10 with 28–29 significant digits, so cents and rates are exact. It's slower; for fees, that's irrelevant. |
| What's banker's rounding? | `MidpointRounding.ToEven`: a value exactly halfway rounds to the nearest even digit (2.5 → 2, 3.5 → 4). It's the default for `Math.Round` in both .NET Framework and modern .NET. It avoids systematic upward bias. Many fee agreements expect `AwayFromZero`. Always pass the mode. |
| You find a bug in the legacy fee calculation. What do you do? | Reproduce it in the new system first (parity). Raise it separately with product and compliance. Quantify the impact per firm. Fix it behind a per-firm flag with sign-off. The migration and the fix are two separately approved changes, or you can't tell which one moved a client's invoice. |
| How do you split a total across N accounts so it sums exactly? | Largest-remainder: floor each share to the cent, then give the leftover cents one at a time to the shares with the largest fractional remainders. Deterministic tie-break (for example by account ID). Define the zero-total case explicitly. |
| Actual/365 or annual ÷ 4? | That's a business and contract question, not a technical one. The engine must reproduce what legacy did (÷ 4) *and* support the basis each firm's agreement specifies. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Core/FeeCalculator.cs` | `double` AUM, `Math.Round(span * (double)tier.AnnualRate / 4, 2)` per tier, `(decimal)fee` at the end |
| `legacy/FeeBilling.Core/HouseholdFeeService.cs` | "Written in decimal, unlike FeeCalculator - the double version gave 'funny cents'", yet still rounds per tier and divides by 4 |
| `legacy/FeeBilling.Core/FlatFeeCalculator.cs` | `Math.Round(annual / 4, 2)` in `decimal` |
| `legacy/FeeBilling.Core/HouseholdAllocator.cs` | `MidpointRounding.AwayFromZero`, and the comment admitting the ±0.01 × (n−1) error and divide-by-zero |
| `legacy/FeeBilling.Web/Web.config` | `Billing.DayCountBasis` = `Annual/4`, and "Documentation only: ... Nothing reads this key." |
| `legacy/FeeBilling.Web/Web.ClientX.config` | "ClientX's fee agreement says actual/365." The contract and the code disagree. |
| `src/FeeBilling.Domain/Entities/FeeTier.cs` | `AnnualRate` "as a fraction: 0.0075 = 0.75%. Stored as decimal(9,6)" |
| `docs/brasswick-modernization-training-plan.md` §5, §10 | Worked examples and scenarios S1–S11 |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | An account with $1,000,296.00 of AUM on `STD-TIERED`. Legacy's `double` path: **2,500.55**. The same algorithm in `decimal`: **2,500.56**. Which one is "right"? For the migration, legacy is, until someone signs off on changing it. |
| 01:30–04:30 | `double` vs `decimal` | `0.1 + 0.2 == 0.3` is false for `double`, true for `decimal`. Why: base 2 vs base 10. The hook explained: 296 × 0.0075 ÷ 4 is exactly 0.555 in `decimal` (rounds to even: 0.56), but slightly below 0.555 in `double` (rounds down: 0.55). The final `(decimal)` cast hides the drift most of the time, which is why this survived a decade. |
| 04:30–07:30 | Legacy arithmetic, three ways | `FeeCalculator` (double, per-tier rounding, ÷ 4), `HouseholdFeeService` (decimal, but the same per-tier rounding and ÷ 4), `FlatFeeCalculator` (decimal, ÷ 4). Three code paths, three numeric behaviours: a parity test per path. |
| 07:30–10:30 | Rounding modes and rounding points | `ToEven` is the default everywhere; the allocator explicitly uses `AwayFromZero`. Someone "tidying up" that argument changes invoices: 3,150.685 becomes 3,150.68 instead of 3,150.69. Per-tier rounding vs rounding once at the end: separate policy dimensions. |
| 10:30–13:30 | Day-count basis | Walk S1: 21,250.00 annual. ÷ 4 = 5,312.50. × 92 / 365 = 5,356.16. **$43.66 per account per quarter.** The config key that was supposed to control this is read by nothing, and ClientX's agreement says actual/365. That's a finding for product and compliance, not a code fix. Also ask: does basis apply to flat fees (S5)? Business decision. |
| 13:30–16:30 | Allocation and the missing penny | Household H-100 (S7) on actual/365: 6,301.37. Proportional `AwayFromZero` allocation gives 3,150.69 + 2,100.46 + 1,050.23 = **6,301.38**. Largest remainder gives 3,150.68 + 2,100.46 + 1,050.23 = **6,301.37**. Zero-AUM household (S8): legacy throws; the new engine must define the behaviour and an ADR must record it. |
| 16:30–19:00 | Policy, not opinion | `BillingPolicy.Legacy` reproduces all of the above, including a `LegacyDoubleArithmetic` flag that deliberately converts through `double`. `BillingPolicy.Corrected` uses `decimal`, the contract's basis, end-of-calculation rounding and largest-remainder allocation. Corrected ships per firm, behind a flag, after sign-off, with the dollar impact per firm in the parity report. |
| 19:00–20:00 | Recap | "We'll fix the rounding while we're in there" is the sentence that fails the interview. Say why. |

### Legacy code, as found

`FeeCalculator.CalculateQuarterlyFee` (excerpt):

```csharp
double fee = 0;
double remaining = aum.Value;
foreach (var tier in account.FeeSchedule.Tiers.OrderBy(t => t.LowerBound))
{
    var span = tier.UpperBound.HasValue
        ? Math.Min(remaining, (double)(tier.UpperBound.Value - tier.LowerBound))
        : remaining;
    fee += Math.Round(span * (double)tier.AnnualRate / 4, 2);  // double + per-tier rounding + /4
    remaining -= span;
    if (remaining <= 0) break;
}
```

`HouseholdAllocator.Allocate`:

```csharp
var total = members.Sum(m => m.BillableAum);
return members.ToDictionary(
    m => m.AccountId,
    m => Math.Round(householdFee * m.BillableAum / total, 2, MidpointRounding.AwayFromZero));
// Sum of allocations can differ from householdFee by ±0.01 × (n-1). Divide-by-zero if total == 0.
```

### Numbers to show on screen

| Expression | Result | Point |
|---|---|---|
| `0.1 + 0.2 == 0.3` / `0.1m + 0.2m == 0.3m` | `False` / `True` | Binary vs decimal |
| `Math.Round(1.015, 2)` / `Math.Round(1.015m, 2)` | `1.01` / `1.02` | The `double` literal is slightly below 1.015 |
| `Math.Round(2.5)` / `Math.Round(2.5, MidpointRounding.AwayFromZero)` | `2` / `3` | Default is `ToEven` |
| `Math.Round(3150.685m, 2)` / with `AwayFromZero` | `3150.68` / `3150.69` | Why the allocator's mode matters |
| Legacy `double` path vs `decimal` path, AUM `1,000,296.00`, `STD-TIERED` | `2500.55` / `2500.56` | Per-tier midpoint lands differently |

These were checked on .NET 10. The golden master (video 04) is what proves how *.NET Framework* computed each seeded account; don't assume every `double` edge case behaves identically on both runtimes. Also note that `double.ToString()` changed in .NET Core 3.0 to print the shortest round-trippable value, so never compare `double` values as strings across runtimes.

### Largest-remainder allocation (sketch)

```csharp
public static IReadOnlyDictionary<int, decimal> LargestRemainder(decimal total, IReadOnlyList<AccountAum> members)
{
    var aumTotal = members.Sum(m => m.BillableAum);
    if (aumTotal == 0m)
    {
        throw new InvalidOperationException("Zero-AUM household: behaviour must be defined by policy (see ADR).");
    }

    var shares = members
        .Select(m =>
        {
            var exact = total * m.BillableAum / aumTotal;
            var floored = Math.Floor(exact * 100m) / 100m;
            return (m.AccountId, Floored: floored, Remainder: exact - floored);
        })
        .ToList();

    var leftoverCents = (int)((total - shares.Sum(s => s.Floored)) * 100m);

    var winners = new HashSet<int>(shares          // Enumerable.ToHashSet isn't in netstandard2.0
        .OrderByDescending(s => s.Remainder)
        .ThenBy(s => s.AccountId)                 // deterministic tie-break
        .Take(leftoverCents)
        .Select(s => s.AccountId));

    return shares.ToDictionary(
        s => s.AccountId,
        s => winners.Contains(s.AccountId) ? s.Floored + 0.01m : s.Floored);
}
```

This sketch assumes a non-negative total already rounded to the cent. Credits (negative totals) need `Math.Ceiling` or sign handling: say so in the interview rather than pretending the edge case doesn't exist.

## Demo

A file-based C# app (`dotnet run check.cs` works with the .NET 10 SDK) that prints the table above. Then reproduce the legacy tier loop twice, once in `double` and once in `decimal`, and search a range of whole-cent AUMs for differences:

```csharp
decimal[] lower = { 0m, 1_000_000m, 5_000_000m };
decimal?[] upper = { 1_000_000m, 5_000_000m, null };
decimal[] rate = { 0.01m, 0.0075m, 0.005m };

double LegacyDouble(decimal aum) { /* FeeCalculator's loop, in double */ }
decimal SameInDecimal(decimal aum) { /* the same loop, in decimal */ }

for (var aum = 1_000_000.00m; aum < 1_020_000.00m; aum += 0.01m)
{
    if ((decimal)LegacyDouble(aum) != SameInDecimal(aum)) Console.WriteLine(aum);
}
```

On .NET 10 that range produces 132 differing AUMs out of 2,000,000, the first at 1,000,296.00. The count is illustrative; the point is that differences are rare, a cent each, and systematic. That's what S11 (500 generated accounts with fractional-cent AUMs) is designed to catch.

Then compute S1 and S7 by hand on screen (training plan §5), and check them against the numbers above.

## Traps to call out

- **"We'll fix the rounding while we're in there."** The migration and the behaviour change must be separable, or nobody can explain a client's changed invoice.
- **Calling `Math.Round` without a `MidpointRounding` argument.** The default is `ToEven`. If that's what you want, say so explicitly; the next person can't tell a decision from an accident.
- **Assuming the `decimal` household path matches the `double` account path.** They share the per-tier rounding and ÷ 4, but not the arithmetic type. Parity-test each path.
- **Treating the day-count basis as a bug.** Legacy's ÷ 4 is what clients have been billed. Actual/365 may be what some contracts say. Both are real; the choice is per firm and signed off.
- **Trusting configuration that nothing reads.** `Billing.DayCountBasis` looks like it controls the basis. It doesn't. Inventory config keys against the code that reads them (video 19).
- **Rounding mid-calculation in the corrected policy.** Round at defined points only: end of the fee calculation, and at allocation.
- **Ignoring the zero-total case.** Legacy throws inside a `TransactionScope`, which fails the whole run (video 16). Define the behaviour, write the ADR.
- **Rate precision.** `0.0075` must survive storage. The scaffolded EF Core mapping uses `decimal(18,2)` for `AnnualRate` (handover issue #2): it would store 0.01. See video 13.

## Key terms

Binary vs decimal floating point · significant digits · banker's rounding (`ToEven`) · `AwayFromZero` · per-tier vs end-of-calculation rounding · day-count basis (annual ÷ 4, actual/365) · largest-remainder allocation · `BillingPolicy.Legacy` / `BillingPolicy.Corrected` · parity mode

## After the video

1. Recompute every Actual/365 value in the training plan's §10 table by hand. The plan tells you to double-check them; report any you disagree with.
2. Write the property "allocations always sum to the household fee" for your largest-remainder implementation, and make sure the generator includes a single member, equal members, and totals that don't divide evenly.
3. Draft the ADR "Parity first, fix behind a flag": the four policy dimensions, the legacy values, the corrected values, and who signs off on each firm.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 5 (worked examples), Section 8 (principle 2: money is `decimal`, rounding is explicit), Section 9 (WP-02), Section 10 (S1–S11)
- Microsoft Learn: *Math.Round* and *MidpointRounding*; *Floating-point numeric types (C# reference)*
- Microsoft: *Floating-point parsing and formatting improvements in .NET Core 3.0*
- David Goldberg, *What Every Computer Scientist Should Know About Floating-Point Arithmetic*
