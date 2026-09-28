# 04 · Golden-Master Characterization Testing

Welcome back. This is the most important lesson in the series, and the one that gives you your strongest interview story. The question is: how do you know the new system is correct? For FeeBilling today, the honest answer is that we don't. In this lesson, we'll build the safety net that changes that answer, before any fee logic moves: run the legacy fee engine exactly as it is, capture its output as a golden master, and make the build fail on any difference nobody can explain.

## The questions this lesson answers

Here's what you'll be able to answer. How do you know the new system is correct? How do you test code that has no tests and can't be unit tested? What's a golden master? The new engine differs from legacy by one cent on seven accounts: what do you do? And why not just fix the failing legacy tests?

The sentence to hold on to is short: legacy output is the specification. Not your opinion of what it should be, not the documentation, not the fee agreement. What legacy actually produced is what clients were actually billed, and that's what the new system has to reproduce first.

## Why the existing tests can't be trusted

Start with what FeeBilling has today. The MSTest project, `legacy/FeeBilling.Tests`, has sixty-one tests. Thirty pass, nine fail, and twenty-two are ignored. Open `.github/workflows/ci.yml` and you'll see the legacy test step runs with continue-on-error set to true, and a comment that says nobody trusts this suite and it doesn't gate merges. And the handover is blunt about it: no parity tests exist for anything fee-related.

It's worth understanding why the suite is in that state, because the reasons tell you why fixing it isn't the answer.

Open `legacy/FeeBilling.Tests/Core/FeeCalculatorTests.cs`. The comment at the top says these tests need the FeeBilling database and an HttpContext, because the fee calculator gets its database context from `HttpContext.Current.Items`. It goes on: the first three were un-ignored in 2019 to "get them running again", and they have been red since. And the period end they test is a static field set to September 30, 2016. So they assert fees for a quarter ten years ago, against whatever data happens to be in the database.

Now search the test project for the ignore attributes. There are four different reasons. "Needs FeeBilling database." "Flaky - cache from previous test." "Depends on today's date." And "GDI+ not available on the build server." Each one points at a design problem in the legacy code: a hidden database dependency, a process-wide cache, a hidden clock, and a Windows-only graphics library. None of those can be fixed without changing legacy code. And changing legacy code before you've captured its behaviour is exactly what you must not do.

So, why not just fix the failing tests? Because making them pass proves nothing about Q3 2026 invoices. Leave them alone, and build the harness that does prove something.

## Characterization testing and the golden master

A characterization test is a test that describes what code actually does, rather than what someone thinks it should do. The term comes from Michael Feathers' book, Working Effectively with Legacy Code. You run the existing code on known inputs, record the outputs, and those recordings become the expected results.

A golden master is the recorded, reviewed output of the current system for a fixed input set. You run the new system on the same inputs and diff the results. Any change to the golden file itself is a deliberate, reviewed act.

For FeeBilling, the fixed input set is the Q3 2026 seed data in `database/seed/010-seed-q3-2026.sql`. The comment at the top of that file maps each account to a scenario. Account 1001 is S1, mid-tier, $2,500,000 on the standard tiered schedule. Account 1002 is S2, exactly on the $1,000,000 boundary. Account 1003 is S3, the minimum fee. 1004 is blended, 1005 is flat, 1006 has a cash exclusion. Accounts 1007 to 1009 are household H-100, and 1010 and 1011 are household H-200, where every member has zero AUM. 1012 opened on August 15. 1013 has a large withdrawal mid-quarter. And accounts 2001 to 2500 are five hundred generated accounts with fractional-cent AUM, designed to flush out floating-point drift.

Here's a design choice that makes the modern side much simpler. Record the inputs as well as the outputs: the billable AUM, the schedule, the household membership. Then the new engine's parity test doesn't need a database at all. It reads the inputs from the golden file, calculates, and compares.

The training plan specifies the columns of the golden file: the account ID, the schedule code, the billable AUM, the fee, the household ID, and the allocated fee for household members. Add one more column, an outcome, which I'll explain in a moment. The file is called `golden/q3-2026.csv`, one row per account, sorted by account ID so that a diff between two versions of it is readable. Every seeded account must have a row. If an account is missing, that's a bug in the runner, not a scenario to skip.

Why a file, rather than assertions in code? Because a file can be reviewed. When someone opens a pull request that changes an invoice amount, the change shows up as a one-line diff in the golden file, next to the code that caused it. That's the kind of evidence an auditor or a compliance officer can actually read.

## Building the legacy runner

The first half of the harness is a small console application. The training plan calls it `tools/FeeBilling.ParityRunner.Legacy`. It doesn't exist yet; building it is work package one. It targets `net472`, because it has to reference `legacy/FeeBilling.Core` and run the real code.

The problem is that the legacy engine can't run outside IIS. Its static calculators get their database context from `DbContextFactory.Current`, and that reads `HttpContext.Current.Items`. Outside a web request, there is no `HttpContext`, so the calculator throws.

The solution is a seam: the narrowest point where you can change the environment without changing the code. The Windows Service already uses one. Open `legacy/FeeBilling.BillingRunner/FakeHttpContext.cs`. It's about ten lines. It creates an `HttpRequest` for localhost, an `HttpResponse` over a string writer, and an `HttpContext` whose user is a generic identity called BillingRunner. The billing runner sets `HttpContext.Current` to that fake before calling the calculators.

Your runner copies that seam. You don't modify legacy code at all. `legacy/README.md` has a section called "Calling the legacy fee engine from your own code" that lists exactly what's needed: a reference to `FeeBilling.Core` and to `System.Web`, an `App.config` with the Entity Framework connection string, and a line setting `HttpContext.Current` to `FakeHttpContext.Create()` on the calling thread before the first call. It also warns that `HttpContext` doesn't flow to other threads. The billing runner learned that the hard way: inside its parallel loop there's a line re-assigning the fake context, with a comment tagged FB-402 saying pool threads have no HttpContext either. And that parallel loop shares a single Entity Framework context across threads, which is its own bug. So your runner is single-threaded. That's deterministic, and it's plenty fast for seed data.

Now the most important detail in this lesson: mirror the production orchestration exactly. It's tempting to call `FeeCalculator.CalculateQuarterlyFee` for each account and record the answer. That would be wrong. Open `legacy/FeeBilling.BillingRunner/BillingRunnerService.cs` and read `ProcessQueue`. For accounts that aren't household-billed, it calls `BillingRunService.CalculateAccountFee`, not the fee calculator directly. And it only processes households if the app setting `Feature.HouseholdBilling` equals the string "true".

Why does that matter? Open `legacy/FeeBilling.Core/BillingRunService.cs`. `CalculateAccountFee` picks the calculator by schedule type, and then applies pro-rating for accounts opened during the quarter, tagged FB-178. If your harness called the fee calculator directly, it would skip the pro-rating, and the golden master would capture something nobody was ever billed.

The runner also has to handle failure as data. If a calculation throws, record the exception type as the outcome for that account, and keep going. Add an outcome column to the golden file for this, with a value of OK or the exception type name. Then "legacy throws" becomes an expected result that the new system must match or consciously change.

And think about culture. Run the calculation in the production culture, because that's how production behaves. But write the golden file in the invariant culture. Otherwise, running it on a laptop set to French Canadian writes the fee as 5312 comma 50 instead of 5312 point 50, and your golden file changes for no reason.

## Quirks to capture, not fix

Now some of the legacy behaviour your golden master will record. The rule is: capture it, don't fix it.

Pro-rating. `CalculateAccountFee` computes days open as the whole days between the period end and the opened-on date, then multiplies the fee by days open and divides by 90. Not by the 92 days actually in Q3. And it counts days exclusively. For S9, an account opened August 15 with $1,000,000, the quarterly fee is $2,500.00. The legacy calculation is 2,500 times 46, divided by 90, which rounds to $1,277.78. A pro-rating based on 47 of 92 days would give $1,277.17. Don't put either number into the test yourself. The golden master records whatever legacy produces. Recomputing by hand is how you explain the difference later.

Per-tier rounding. Every tier is rounded separately, after dividing by four. That's covered in detail in the next lesson.

Precision drift. The five hundred generated accounts in S11 exist because the tiered calculator works in `double`. For most amounts, the final cast back to `decimal` hides the drift. For a handful, a midpoint lands on the other side, and the fee is a cent different from what the same arithmetic in `decimal` would give. You won't predict which accounts those are. The golden master will simply record them, and the parity report will show exactly which ones need the legacy double behaviour to match.

Flows. `AumService.cs` has a to-do tagged FB-212 saying large mid-quarter flows should pro-rate, but for now it bills on the period-end market value only. So S10 is billed on period-end AUM.

Zero-AUM households. For S8, the household fee is the minimum, and then `HouseholdAllocator.Allocate` divides by the total AUM, which is zero. It throws a divide-by-zero exception. Record it.

And the cache. The fee calculator caches AUM in `HttpRuntime.Cache` for thirty minutes, with a key built from the date's short string form, which depends on culture, and an expiry based on local time. Two runs in the same process can share stale AUM. So run each golden generation in a fresh process, against freshly seeded data.

## The modern side, and governance

The second half of the harness lives in the modern solution. The training plan calls it `tests/FeeBilling.Parity.Tests`, targeting `net10.0`, using xUnit like the other modern tests. It loads the golden inputs, runs the new engine with a legacy policy preset, and compares each result with the recorded output.

Compare as `decimal`, never as strings. Here's why. The legacy tiered path calculates in `double` and casts to `decimal` at the end, so a fee comes back as 5312.5, with one decimal place. The decimal path rounds to two places and gives 5312.50. They're equal values with a different scale, so a string comparison would fail. Write the file with a fixed two-decimal format, parse it back as `decimal`, and compare numerically.

The output is a parity report: a summary of exact matches, then one row per difference with the account, the legacy value, the new value, the delta and a category. The categories from the training plan are double precision, rounding mode, day count, and allocation penny.

How do you assign a category? Re-run the failing account with one policy dimension switched back to legacy behaviour at a time. If switching on the legacy double arithmetic makes the difference disappear, it's double precision. If switching back to per-tier rounding and banker's rounding does it, it's rounding mode. If switching back to divide-by-four does it, it's day count. If switching back to proportional allocation does it, it's the allocation penny. And if none of them explain it, it's unexplained, and it fails the build.

Explained differences aren't automatically fine, either. Each one needs an entry in a known-differences allowlist, linked to an ADR or a ticket, and with the legacy policy there should be zero differences at all.

Finally, governance. The golden files are committed. Changing one is a reviewed act: put a code-owners rule on the golden folder so the right people must approve. The legacy runner only runs on Windows, and only when someone deliberately regenerates the golden master. The parity tests run on every pull request, in the Linux job.

When does the golden file legitimately change? When the seed data changes, for example when you add a scenario. The process is the same every time. Re-seed the database, regenerate on Windows in a fresh process, and review the diff line by line. New rows for the new scenario are expected. Any change to an existing row is a red flag, because the legacy code hasn't changed, so its output shouldn't either. If an existing row moves, find out why before you commit it. And if you want less plumbing, approval-testing libraries such as Verify give you the same record-and-compare workflow; check which package matches your test framework.

## Traps

Here are the traps.

Refactoring legacy to make it testable first. Copy the seam into the runner and leave the legacy folder alone.

Calling the fee calculator directly. Pro-rating lives in the orchestrator, and household billing depends on a config flag. Mirror the orchestration.

Multithreading the runner. `HttpContext.Current` doesn't flow to other threads, and legacy's parallel loop shares one database context.

Letting the cache leak between runs. Fresh process, fresh data, every time.

Writing the golden file in the current culture, or comparing money as strings.

Letting "legacy throws" crash the runner instead of recording it.

And accepting golden-file changes in one big unexplained diff.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do you know the new system is correct?

[pause 5s]

Three layers. Golden-master tests on the fee engine: legacy output captured for a fixed data set, and the new engine diffed against it. Contract tests on the APIs, so the front end and other consumers see the same shapes. And shadow runs in production for a full billing cycle, where the new system calculates alongside legacy and we diff the results. Every difference is either fixed or explicitly accepted, with sign-off and a documented reason.

**Interviewer:** How do you test code that has no tests and can't be unit tested?

[pause 5s]

Characterization tests. I find the narrowest seam that lets me run the code unchanged. In FeeBilling, that's a fake `HttpContext`, copied from the Windows Service. I feed it known inputs, record the outputs, and those become the expected results. I don't refactor it first, because then I'd be measuring my changes, not the production behaviour.

**Interviewer:** What's a golden master?

[pause 5s]

A recorded, reviewed output of the current system for a fixed input set. The new system runs on the same inputs, and any difference fails the build unless it's categorized and on an allowlist linked to an ADR. Changes to the golden file are deliberate and reviewed, for example through a code-owners rule. I also record the inputs, so the modern test doesn't need a database.

**Interviewer:** The new engine differs from legacy by one cent on seven accounts. What do you do?

[pause 5s]

Categorize each difference by switching one policy dimension back to legacy behaviour at a time: double precision, rounding mode, day count, or allocation penny. Under the legacy policy the target is zero differences, so I fix the new engine until it reproduces legacy exactly. Anything we intend to change goes on an allowlist with an ADR, the dollar impact per firm, and business sign-off, and ships separately behind a flag.

**Interviewer:** Why not just fix the failing legacy tests?

[pause 5s]

Because they assert 2016 data, they need a database and an `HttpContext`, some share a process-wide cache, and some depend on today's date. Making them pass proves nothing about this quarter's invoices, and fixing them means changing legacy code before capturing its behaviour. I'd leave them alone and build the harness that actually proves parity.

## Recap

Five things to remember from this lesson.

One: legacy output is the specification. The golden master records what clients were actually billed, bugs included.

Two: use a seam, not a refactor. In FeeBilling, that's the fake `HttpContext`, copied into a single-threaded `net472` runner.

Three: mirror the production orchestration. Pro-rating and household billing live outside the fee calculator.

Four: write golden files in the invariant culture, record inputs and exceptions, and compare money numerically.

Five: every difference gets a category; unexplained differences fail the build; golden-file changes are reviewed.

In the next lesson, we'll look at what the parity report will find: `double` versus `decimal`, banker's rounding, day-count basis, and the missing penny in household allocation.
