# 04 · Golden-Master Characterization Testing

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-01 · **Prerequisites:** 01, 02

## Why this video exists

"How do you know the new system is correct?" is the question that decides a modernization interview for a billing system. The honest answer for FeeBilling today is "we don't". The legacy suite has 61 tests, 9 fail and 22 are ignored, and CI runs it with `continue-on-error`. The previous team's handover says it plainly: "No parity tests exist for anything fee-related." This video builds the safety net before anything moves: run the legacy fee engine as-is, capture its output as a golden master, and make CI fail on any unexplained difference.

## Learning objectives

By the end, the viewer can:

- Explain characterization testing: the legacy output *is* the specification, bugs included.
- Call untestable static code from a harness through a seam, without refactoring it first.
- Mirror the production orchestration exactly, so the golden master captures what clients were actually billed.
- Write golden files that don't change with the machine's culture, and compare them numerically, not as strings.
- Produce a parity report that categorizes every difference, with an allowlist tied to ADRs or tickets.
- Put golden files under change control, and gate CI on them.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do you know the new system is correct? | Three layers: golden-master tests on the engine, contract tests on the APIs, and shadow runs in production for a full billing cycle. Every difference is either fixed or explicitly accepted with sign-off. |
| How do you test code that has no tests and can't be unit tested? | Characterization tests. Find the narrowest seam that lets you run it unchanged (here, a fake `HttpContext`), feed it known inputs, record the outputs. Don't refactor it to make it testable before you've captured its behaviour; you'd be changing the thing you're measuring. |
| What's a golden master? | A recorded, reviewed output of the current system for a fixed input set. The new system is run on the same inputs and diffed. Changes to the golden file are deliberate, reviewed acts. |
| The new engine differs from legacy by one cent on 7 accounts. What do you do? | Categorize each difference (double precision, rounding mode, day count, allocation penny). Reproduce legacy exactly under the legacy policy. Anything you intend to change goes on an allowlist linked to an ADR, with the dollar impact per firm, and ships separately behind a flag. |
| Why not just fix the failing legacy tests? | They assert 2016 data, need a database and an `HttpContext`, and some depend on today's date. Making them pass proves nothing about Q3 2026 invoices. Leave them; build the harness that does. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Tests/Core/FeeCalculatorTests.cs` | "The first three were un-ignored in 2019 to 'get them running again'. They have been red since." `PeriodEnd = new DateTime(2016, 9, 30)` |
| `legacy/FeeBilling.Tests/**` | The four `Ignore` reasons: "Needs FeeBilling database", "Flaky - cache from previous test", "Depends on today's date", "GDI+ not available on the build server" |
| `.github/workflows/ci.yml` | `continue-on-error: true` on the legacy test step |
| `legacy/README.md` ("Calling the legacy fee engine from your own code") | The seam: reference `System.Web`, an `App.config` connection string, `HttpContext.Current = FakeHttpContext.Create();` on the calling thread |
| `legacy/FeeBilling.BillingRunner/FakeHttpContext.cs` | The 10-line seam to copy into the runner |
| `legacy/FeeBilling.BillingRunner/BillingRunnerService.cs` | `ProcessQueue`: the orchestration the harness must mirror |
| `legacy/FeeBilling.Core/BillingRunService.cs` | `IsHouseholdBilled`, and the FB-178 pro-rating inside `CalculateAccountFee` |
| `database/seed/010-seed-q3-2026.sql` | The account-to-scenario map (S1–S11) |
| `tools/FeeBilling.ParityRunner.Legacy/` *(future, WP-01)* | The `net472` console runner you build |
| `tests/FeeBilling.Parity.Tests/` *(future, WP-01)* | The `net10.0` test project that diffs against the golden file |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | 61 tests, 9 failing, 22 ignored, and CI doesn't care. What is protecting 350,000 accounts' invoices? Nothing. That's the first problem to fix, before any code moves. |
| 01:30–04:00 | Why the suite can't be trusted | Walk the ignore reasons and the three red tests. They test 2016 period ends, need a database and `HttpContext`, share a process-wide cache, and read the system clock. None of that can be fixed without changing legacy. |
| 04:00–06:30 | Characterization testing | Legacy output is the spec, not your opinion of what it should be. Golden master = recorded inputs *and* outputs for a fixed data set (the Q3 2026 seed). Recording inputs too means the new engine's parity test needs no database. |
| 06:30–11:00 | Building the legacy runner | `net472` console, single thread. Copy the `FakeHttpContext` seam; don't touch legacy. Mirror `ProcessQueue`: split accounts with `IsHouseholdBilled`, call `BillingRunService.CalculateAccountFee` (not `FeeCalculator` directly: pro-rating lives in the orchestrator), and run households only if `Feature.HouseholdBilling` is `"true"`. Record exceptions as data. Run in the production culture; write the file in the invariant culture. |
| 11:00–13:30 | Quirks to capture, not fix | Pro-rating divides by `90m`, not the 92-day quarter, and counts days exclusively. Every tier rounds separately. S10 bills on period-end AUM only (FB-212). S8 throws `DivideByZeroException`. All of it goes into the golden file exactly as legacy does it. |
| 13:30–17:00 | The modern side | `tests/FeeBilling.Parity.Tests` loads the golden inputs, runs the new engine with `BillingPolicy.Legacy`, and compares as `decimal`. Produce a parity report. Attribute each difference by toggling one policy dimension at a time (below). Unexplained differences fail the build; explained ones need an allowlist entry pointing at an ADR or ticket. |
| 17:00–19:00 | Governance | Golden files are committed; changing one is a reviewed act (CODEOWNERS on the folder). The runner only runs on Windows, and only when someone deliberately regenerates. Parity tests run on every PR in the `ubuntu-latest` job. Approval-testing libraries such as Verify give you the same workflow with less plumbing. |
| 19:00–20:00 | Recap | "Legacy output is the specification." Preview video 05: what the parity report will find. |

### The seam and the orchestration

The seam, as found (`legacy/FeeBilling.BillingRunner/FakeHttpContext.cs`):

```csharp
public static HttpContext Create()
{
    var request = new HttpRequest(string.Empty, "http://localhost/", string.Empty);
    var response = new HttpResponse(new StringWriter());
    var context = new HttpContext(request, response)
    {
        User = new GenericPrincipal(new GenericIdentity("BillingRunner"), new string[0])
    };
    return context;
}
```

The orchestration to mirror (`BillingRunnerService.ProcessQueue`, excerpt):

```csharp
Parallel.ForEach(accounts.Where(a => !BillingRunService.IsHouseholdBilled(a)), acct =>   // shared DbContext across threads
{
    HttpContext.Current = _fakeContext;   // FB-402: pool threads have no HttpContext either

    var fee = BillingRunService.CalculateAccountFee(acct.Id, run.PeriodEnd);
```

```csharp
if (ConfigurationManager.AppSettings["Feature.HouseholdBilling"] == "true")
{
```

The pro-rating the harness would miss if it called `FeeCalculator` directly (`BillingRunService.CalculateAccountFee`):

```csharp
// FB-178: accounts opened during the quarter only pay for the days they were open.
var quarter = BillingPeriod.ForQuarterEnding(periodEnd);
if (account.OpenedOn > quarter.Start)
{
    var daysOpen = (periodEnd - account.OpenedOn).Days;
    fee = Math.Round(fee * daysOpen / 90m, 2);
```

For S9 (opened August 15, $1,000,000) that's `2,500.00 × 46 / 90 = 1,277.78`. A 47-of-92-days pro-rating would give 1,277.17. Don't compute this cell yourself in the test: **the golden master records whatever legacy produces**. Recomputing by hand is how you *explain* the difference later.

### Runner sketch (`tools/FeeBilling.ParityRunner.Legacy`, `net472`, C# 7.3)

```csharp
internal static class Program
{
    private static int Main(string[] args)
    {
        var periodEnd = new DateTime(2026, 9, 30);
        HttpContext.Current = FakeHttpContext.Create();   // copied seam; single thread only

        var rows = new List<GoldenRow>();
        using (var db = new FeeBillingEntities())
        {
            foreach (var account in db.Accounts.OrderBy(a => a.Id).ToList())
            {
                if (BillingRunService.IsHouseholdBilled(account)) continue;   // households handled below
                rows.Add(Capture(account.Id, () => BillingRunService.CalculateAccountFee(account.Id, periodEnd)));
            }
            // ...households via HouseholdFeeService.CalculateAndAllocate, only if Feature.HouseholdBilling == "true"
        }

        GoldenFile.Write("golden/q3-2026.csv", rows);   // invariant culture, "0.00" formats
        return 0;
    }

    private static GoldenRow Capture(int accountId, Func<decimal> calculate)
    {
        try { return GoldenRow.Ok(accountId, calculate()); }
        catch (Exception ex) { return GoldenRow.Threw(accountId, ex.GetType().Name); }   // S8 is data, not a crash
    }
}
```

Golden columns, from the training plan, plus one: `AccountId, ScheduleCode, BillableAum, Fee, HouseholdId, AllocatedFee, Outcome`. `Outcome` (`OK` or an exception type name) lets "legacy throws" be an expected result.

### Parity test sketch (`tests/FeeBilling.Parity.Tests`, `net10.0`, xUnit v3)

```csharp
[Fact]
public void Legacy_policy_reproduces_the_golden_master()
{
    var golden = GoldenFile.Load("golden/q3-2026.csv");
    var report = ParityComparer.Compare(golden, FeeEngine.Create(BillingPolicy.Legacy));

    File.WriteAllText("parity-report.md", report.ToMarkdown());
    Assert.Empty(report.Differences.Where(d => !KnownDifferences.IsAllowed(d)));
}
```

### Attributing a difference to a category

Re-run the failing account with one `BillingPolicy` dimension switched back to legacy behaviour at a time. The dimension that makes the difference disappear is the category.

| Switch back to legacy | Difference disappears? | Category |
|---|---|---|
| `LegacyDoubleArithmetic = true` | Yes | `DOUBLE_PRECISION` |
| `Rounding = LegacyPerTier`, `Midpoint = ToEven` | Yes | `ROUNDING_MODE` |
| `Basis = AnnualDividedByFour` | Yes | `DAY_COUNT` |
| `Allocation = LegacyProportional` | Yes | `ALLOCATION_PENNY` |
| None of the above | — | **Unexplained**: fails CI |

## Demo

```bash
docker compose up -d
dotnet run --project tools/FeeBilling.DbInit -- --reseed

# The suite nobody trusts
dotnet test legacy/FeeBilling.Tests            # Failed 9, Passed 30, Skipped 22
git grep -n "Ignore(" -- legacy/FeeBilling.Tests

# Where the fee is actually computed for an account with its own schedule
git grep -n "CalculateAccountFee\|IsHouseholdBilled\|Feature.HouseholdBilling" -- legacy

# Future (after WP-01): regenerate on Windows, diff on any OS
dotnet run --project tools/FeeBilling.ParityRunner.Legacy
git diff --stat golden/
dotnet test tests/FeeBilling.Parity.Tests
```

## Traps to call out

- **Refactoring legacy to make it testable first.** The whole point is to measure the code as it runs in production. Copy the seam into the runner; leave `legacy/` alone.
- **Calling `FeeCalculator` directly.** Pro-rating lives in `BillingRunService.CalculateAccountFee`, and household billing depends on a config flag. Mirror the orchestration, or the golden master captures something nobody was billed.
- **Multithreading the runner.** `HttpContext.Current` doesn't flow to other threads, and legacy's `Parallel.ForEach` shares one `DbContext`. Single-threaded is deterministic and fast enough for seed data.
- **Letting the process-wide cache leak between runs.** `HttpRuntime.Cache` holds AUM for 30 minutes (local-time expiry) under a culture-dependent key. Run each golden generation in a fresh process against freshly seeded data.
- **Writing the CSV in the current culture.** A `fr-CA` laptop writes `5312,50`. Use `CultureInfo.InvariantCulture` for the file, and the production culture for the calculation.
- **String-comparing money.** `(decimal)5312.5d` is `5312.5` and the decimal path gives `5312.50`: equal values, different scale. Format with `"0.00"`, parse back to `decimal`, compare numerically.
- **Letting "legacy throws" crash the runner.** Record the exception type as the expected outcome for S8.
- **Accepting golden-file changes in a big diff.** Every change to `golden/` is reviewed, explained, and linked to a reason.

## Key terms

Characterization test · golden master · approval testing · seam · parity report · difference category · known-differences allowlist · CODEOWNERS

## After the video

1. Build `tools/FeeBilling.ParityRunner.Legacy` and generate `golden/q3-2026.csv` for S1–S11. Check that every seeded account has a row.
2. Predict, by hand, the legacy values for S1, S2, S3 and S9. Compare with the golden file and explain any surprise.
3. Write the parity report format: summary counts, then one row per difference with account, legacy value, new value, delta and category.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 9 (WP-01), Section 10 (seed scenarios) and Section 12.2 ("How do you know the new system is correct?")
- `docs/handover.md` (known issue #5, "Things we'd have done next")
- `legacy/README.md` ("Calling the legacy fee engine from your own code")
- Michael Feathers, *Working Effectively with Legacy Code* (characterization tests, seams)
- Verify (approval testing library for .NET): check the package that matches your test framework
