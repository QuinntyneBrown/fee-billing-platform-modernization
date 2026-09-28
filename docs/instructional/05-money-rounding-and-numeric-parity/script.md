# 05 · Money, Rounding and Numeric Parity

Welcome back. Fee billing is arithmetic that clients and regulators audit. The FeeBilling engine does that arithmetic in `double`, rounds at every tier, divides by four regardless of what the fee agreement says, and allocates household fees in a way that can create a cent out of nowhere. Every one of those is a trap in a migration, because the moment someone "cleans it up", invoices change. In this lesson, we'll go through each numeric behaviour in the legacy code, prove it with real numbers, and build the answer to one of the best interview questions you can get: you find a bug in the legacy fee calculation, what do you do?

## The questions this lesson answers

Here's what you'll be able to answer. Why use `decimal` for money? What's banker's rounding? You find a bug in the legacy fee calculation: what do you do? How do you split a total across several accounts so the parts add up exactly? And should the quarterly fee be the annual fee divided by four, or actual days over 365?

Here's the sentence that fails this part of the interview: "We'll fix the rounding while we're in there." By the end of this lesson, you'll be able to explain exactly why.

## Double versus decimal

Let's start from first principles. `double` is binary floating point. It stores numbers as a binary fraction times a power of two. Most decimal fractions, like one tenth, or a fee rate of 0.0075, have no exact binary representation, just as one third has no exact decimal representation. So the stored value is very slightly off, and arithmetic on it drifts.

The classic demonstration: in `double`, 0.1 plus 0.2 is not equal to 0.3. In `decimal`, it is. `decimal` is a base-ten type with twenty-eight to twenty-nine significant digits, so cents and rates are represented exactly. It's slower than `double`, but for fee calculations the speed difference is irrelevant.

Drift matters most at midpoints, because rounding decides which side of the midpoint a value is on. Here's a small example. Round the number 1.015 to two places. As a `double`, the answer is 1.01, because the stored value is actually slightly below 1.015. As a `decimal`, it's exactly 1.015, which is a midpoint, and the answer is 1.02.

Now a FeeBilling example. Take an account with $1,000,296.00 of AUM on the standard tiered schedule. The first tier is $1,000,000 at 1 percent, divided by four: $2,500.00 exactly. The second tier is the remaining $296 at 0.75 percent, divided by four. In `decimal`, that's exactly 0.555, a midpoint, which rounds to 0.56. In `double`, it's a hair below 0.555, which rounds down to 0.55. So the legacy double path produces $2,500.55, and the same algorithm in `decimal` produces $2,500.56.

Which one is right? For the migration, legacy is right, until someone signs off on changing it.

One caution about those numbers. They were checked on .NET 10. The legacy code runs on .NET Framework, and you shouldn't assume every `double` edge case behaves identically on both runtimes. That's another reason the golden master matters: it records how .NET Framework actually computed each seeded account, so you don't have to reason about it. And never compare `double` values as strings across runtimes. In .NET Core 3.0, the default formatting of `double` changed to print the shortest string that round-trips, so the same value can print differently on the old and new runtimes. And notice why this survived for a decade: the final cast from `double` to `decimal` hides the drift almost all the time. Scanning every whole-cent AUM from one million to just over one million twenty thousand dollars, on .NET 10, about 132 values out of two million differ. Rare, a cent each, and systematic. That's exactly what the five hundred generated accounts in scenario S11 are designed to catch.

## Three code paths, three behaviours

Now look at the legacy code, because "legacy behaviour" isn't one thing.

Open `legacy/FeeBilling.Core/FeeCalculator.cs`. It reads the AUM, casts it to `double`, and loops over the tiers. For each tier, it adds `Math.Round` of the span times the rate divided by four, to two places. The comment on that line says it plainly: double, plus per-tier rounding, plus divide by four. Then it applies the minimum: the minimum annual fee, also converted to `double` and divided by four. If the fee is below that, the fee becomes the minimum. At the end, it casts the result back to `decimal`.

`legacy/FeeBilling.Core/BlendedFeeCalculator.cs` is a copy of that file, and it also works in `double`. But it rounds differently. A blended schedule charges the whole AUM at the rate of the highest tier reached, so there's only one multiplication, and it rounds once: the AUM times the rate, divided by four, rounded to two places. So even the two `double` paths don't round at the same points.

The minimum fee has its own subtlety. Take scenario S3: an account with $80,000 on the standard tiered schedule. One percent of $80,000 is $800 a year, which is below the $1,000 annual minimum. Under divide-by-four, the quarterly minimum is $250.00. On actual over 365, it's $252.05. So the minimum is applied after the day-count basis, and the basis changes the minimum too. That's the kind of ordering detail the golden master pins down for you.

Now open `legacy/FeeBilling.Core/HouseholdFeeService.cs`. The comment says it was written in decimal, unlike FeeCalculator, because the double version gave "funny cents" on the household statements. So someone noticed the problem in 2015 and fixed it for households only. But look closer: it still rounds per tier, and it still divides by four. So it's a different numeric behaviour from the account path, not the same behaviour in a better type.

And `legacy/FeeBilling.Core/FlatFeeCalculator.cs` works in `decimal`, taking the flat annual fee, dividing by four, and rounding to two places.

Three code paths, three numeric behaviours. The practical consequence is that you need a parity test for each path. Don't assume the household path matches the account path just because they share a loop shape.

## Rounding modes and rounding points

Next, rounding. There are two separate questions: how you round a midpoint, and where in the calculation you round.

First, midpoints. `Math.Round` without a mode argument uses `MidpointRounding.ToEven`, also called banker's rounding. A value exactly halfway rounds to the nearest even digit. So 2.5 rounds to 2, and 3.5 rounds to 4. That's the default in both .NET Framework and modern .NET, and it exists to avoid a systematic upward bias when you round lots of numbers. The alternative most people learned at school is `MidpointRounding.AwayFromZero`, where 2.5 rounds to 3. Many fee agreements expect that one.

Now open `legacy/FeeBilling.Core/HouseholdAllocator.cs`. It explicitly passes `MidpointRounding.AwayFromZero`. Imagine a developer tidying up and deleting that argument because it looks redundant. The allocation for one account in household H-100 is 3,150.685. With away-from-zero, it rounds to 3,150.69. With the default, it rounds to 3,150.68. One tidy-up, and an invoice changes. That's why the rule in the new code is: every `Math.Round` in money code passes the mode explicitly, even when it's the default. The next reader can't tell a decision from an accident otherwise.

Second, rounding points. Legacy rounds every tier separately, after dividing by four. The alternative is to calculate the whole annual fee exactly, apply the period factor, and round once at the end. Those give different answers, by a cent here and there. They're separate policy dimensions, and you'll want to switch them independently.

## Day-count basis

Now the biggest money difference of all, and it isn't a rounding question.

A quarterly fee can be calculated two ways. Annual divided by four treats every quarter as exactly a quarter of a year. Actual over 365 multiplies the annual fee by the number of days in the period, divided by 365. Q3 2026 has 92 days.

Walk through scenario S1: $2,500,000 on the standard tiered schedule. The annual fee is 1 percent of the first million, which is $10,000, plus 0.75 percent of the next $1,500,000, which is $11,250, for a total of $21,250. Divided by four, that's $5,312.50. Times 92 over 365, it's $5,356.16. That's a difference of $43.66, per account, per quarter.

Legacy always divides by four. Now open `legacy/FeeBilling.Web/Web.config`. There's a setting called `Billing.DayCountBasis` with the value Annual slash 4, and a comment that says it's documentation only: the fee calculators always divide by four, and nothing reads this key. Then open `legacy/FeeBilling.Web/Web.ClientX.config`, the transform for one enterprise client. It sets the same key to Actual slash 365, with a comment saying ClientX's fee agreement says actual over 365, and that nothing reads the key.

So the contract says one thing, and the code does another. Is that a bug? It's a finding for product and compliance, not a code fix. Legacy's divide-by-four is what that client has actually been billed. The migration must reproduce it, and the new engine must also support the basis each firm's agreement specifies, so the business can decide what happens next.

There's a follow-on business question too: does the day-count basis apply to flat fees? Scenario S5 is a flat $2,000 a year. Divided by four, it's $500.00. On actual over 365, it's $504.11. Neither answer is technically wrong. It's a decision someone has to make and sign.

Get into the habit of recomputing these by hand. The training plan's scenario table lists an actual-over-365 value for every scenario, and it explicitly tells you to double-check them. For S2, an account exactly on the $1,000,000 boundary, the annual fee is $10,000. Divided by four, that's $2,500.00. Times 92 over 365, it's $2,520.55. If you can do that arithmetic out loud in an interview, you show that you check expected values instead of trusting them, which is exactly the habit a billing system needs.

## Allocation and the missing penny

Now households. When linked accounts are billed together, the household fee is calculated on their combined AUM, then allocated back to each account in proportion to its AUM.

Take household H-100, scenario S7. Three accounts: A with $1,500,000, B with $1,000,000, C with $500,000. On actual over 365, the quarterly household fee is $6,301.37. Now allocate it proportionally with away-from-zero rounding, the way the legacy allocator does. A gets half: 3,150.685, which rounds to $3,150.69. B gets a third: $2,100.46. C gets a sixth: $1,050.23. Add them up and you get $6,301.38. One cent more than the household fee. The comment in the legacy allocator admits this: the sum of allocations can differ from the household fee by up to a cent for every member after the first.

The correct technique is largest-remainder allocation. Floor each share to the cent: $3,150.68, $2,100.45 and $1,050.22, which adds up to $6,301.35. That leaves two cents. Hand them out one at a time to the shares with the largest fractional remainders. C's remainder is about 0.83 of a cent, and B's is about 0.67, so C and B each get a cent. The result is $3,150.68, $2,100.46 and $1,050.23, which adds up to exactly $6,301.37. Use a deterministic tie-break, for example by account ID, so the same inputs always produce the same allocation. And if you ever handle credits, negative totals need their own sign handling. Say so rather than pretending the edge case doesn't exist.

How do you know your allocation is right for every input, not just H-100? Write it as a property-based test. The property is simple: for any household fee and any set of member AUMs, the allocations always add up to the household fee. Let the test library generate thousands of cases, and make sure the generator includes a single member, members with equal AUM, and totals that don't divide evenly. Those are the cases where hand-written examples usually miss something.

There's one more edge case: scenario S8, a household where every account has zero AUM. The legacy allocator divides by the total, which is zero, and throws. The new engine must define that behaviour explicitly, and an ADR must record the decision.

## Policy, not opinion

So how does the new engine deal with all of this? With an explicit policy object. The training plan calls it `BillingPolicy`, with four dimensions: day-count basis, rounding point, midpoint rounding mode, and allocation method.

`BillingPolicy.Legacy` reproduces everything we've just heard: divide by four, per-tier rounding, the legacy midpoint behaviour, and proportional allocation. It even includes a flag called `LegacyDoubleArithmetic` that deliberately converts through `double` on the paths that legacy computed in `double`. Reproducing a bug on purpose feels wrong. It's correct, and it's temporary. Document why the flag exists and when it will be removed.

`BillingPolicy.Corrected` uses `decimal` throughout, the basis the contract specifies, rounding once at the end, and largest-remainder allocation. It ships per firm, behind a flag, after sign-off, with the dollar impact per firm in the parity report.

And the answer to the interview question falls out of that design. You can't change billing behaviour for 350,000 accounts as a side effect of a framework upgrade. The migration and the fix are two separate, separately approved changes. Otherwise, when a client's invoice changes, nobody can tell which change caused it.

## Traps

Here are the traps.

"We'll fix the rounding while we're in there." The migration and the behaviour change must be separable.

Calling `Math.Round` without a mode. Always pass `MidpointRounding`, even when you want the default.

Assuming the household path matches the account path. Different types, same per-tier rounding. Test each path.

Treating the day-count basis as a bug. Both answers are real; the choice is per firm, and signed off.

Trusting configuration that nothing reads, like `Billing.DayCountBasis`.

Rounding in the middle of the corrected calculation. Round at defined points only.

Ignoring the zero-total household.

And rate precision. The rate 0.0075 must survive storage. `src/FeeBilling.Domain/Entities/FeeTier.cs` says the rate is stored as decimal nine comma six, but the scaffolded EF Core mapping uses eighteen comma two, which would store 0.01. That's lesson thirteen.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** Why use `decimal` for money?

[pause 5s]

`double` is binary floating point, so most decimal fractions, like 0.1 or a rate of 0.0075, can't be represented exactly. Arithmetic drifts, and midpoints land on the wrong side when you round. `decimal` is base ten with twenty-eight to twenty-nine significant digits, so cents and rates are exact. In FeeBilling, an account with $1,000,296.00 of AUM gets $2,500.55 on the legacy double path and $2,500.56 in decimal. It's slower, but for fees that doesn't matter.

**Interviewer:** What's banker's rounding?

[pause 5s]

It's `MidpointRounding.ToEven`: a value exactly halfway rounds to the nearest even digit, so 2.5 goes to 2 and 3.5 goes to 4. It's the default for `Math.Round` in both .NET Framework and modern .NET, and it avoids an upward bias. Many fee agreements expect away-from-zero instead. So I always pass the mode explicitly. In FeeBilling, the household allocator relies on away-from-zero, and removing that argument would change invoices.

**Interviewer:** You find a bug in the legacy fee calculation. What do you do?

[pause 5s]

I reproduce it in the new system first, so the parity test passes with the legacy policy. I raise the bug separately with product and compliance, and quantify the impact per firm from the parity report. Then the fix ships behind a per-firm flag, with sign-off. The migration and the fix are two separately approved changes, otherwise nobody can tell which one moved a client's invoice.

**Interviewer:** How do you split a total across several accounts so it sums exactly?

[pause 5s]

Largest-remainder allocation. Floor each share to the cent, work out how many cents are left over, and give them one at a time to the shares with the largest fractional remainders, with a deterministic tie-break such as account ID. For household H-100, proportional rounding gives $6,301.38 for a $6,301.37 fee; largest remainder gives exactly $6,301.37. And I'd define the zero-total case explicitly, because legacy divides by zero.

**Interviewer:** Actual over 365, or annual divided by four?

[pause 5s]

That's a business and contract question, not a technical one. For a $2,500,000 account in Q3 2026, it's the difference between $5,312.50 and $5,356.16. Legacy always divides by four, even though one client's agreement says actual over 365. The new engine must reproduce legacy exactly and support a per-firm basis, so the business can decide, with sign-off, what each firm should be billed.

## Recap

Five things to remember from this lesson.

One: money is `decimal`. `double` drifts, and drift shows up as a one-cent difference at midpoints.

Two: always pass `MidpointRounding`. The default is banker's rounding, and the legacy allocator depends on away-from-zero.

Three: rounding point and day-count basis are separate policy dimensions. Divide-by-four versus actual over 365 is $43.66 per account per quarter for S1, and it's a business decision.

Four: largest-remainder allocation makes household allocations sum exactly. Define the zero-total case.

Five: reproduce legacy first with `BillingPolicy.Legacy`, then ship `BillingPolicy.Corrected` per firm, behind a flag, with sign-off.

In the next lesson, we'll design the new fee engine itself: a pure domain, with strategies, an explicit policy, and a calculation trace on every result.
