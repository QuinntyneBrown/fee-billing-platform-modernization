# 06 · A Pure-Domain Fee Engine

Welcome back. In the last two lessons, we captured what the legacy fee engine does, and we understood every numeric quirk in it. Now we design its replacement. The legacy fee logic is a handful of static classes that reach into the web request, the ASP.NET cache, a stored procedure and a logging library, and one of them is a copy-paste of another. It can't be unit tested, it can't run outside IIS without a fake request, and it can't run in a Linux container at all. By the end of this lesson, you'll be able to explain how to make logic like that testable without changing its behaviour, and describe the design of a pure fee engine whose behaviour is selected by an explicit policy and proven by the golden master.

## The questions this lesson answers

Here's what you'll be able to answer. How do you make static, untestable business logic testable without changing its behaviour? Where does the clock go? Strategy pattern or a switch statement? How do you keep a domain layer pure over time? And how do you make every calculation auditable?

The design principle for this lesson comes from the training plan's target architecture: the domain is pure. It has no references to Entity Framework, ASP.NET, logging frameworks, or the clock. Inputs go in, invoice lines come out. That's what makes parity testing possible.

## Anatomy of a static calculator

Let's start by taking the legacy calculator apart, because naming its hidden dependencies is half the interview answer.

Open `legacy/FeeBilling.Core/FeeCalculator.cs`. The method `CalculateQuarterlyFee` takes an account ID and a period end. That signature looks simple. It isn't. Here's everything the method actually depends on.

One: an ambient database context. The first line gets `DbContextFactory.Current`, and the comment says it's pulled from `HttpContext.Current.Items`. Open `legacy/FeeBilling.Data/DbContextFactory.cs` and the summary says it outright: anything that runs outside IIS, like the billing runner, tools or tests, has to fake an HttpContext first.

Two: Entity Framework 6, with a string-based include of the fee schedule and its tiers.

Three: a stored procedure. The AUM comes from `AumService.GetBillableAum`, which runs `usp_GetBillableAum` against the database. So the database isn't just where the account lives; it's inside the calculation.

Four: a process-wide cache. The AUM is cached in `HttpRuntime.Cache` under a key built with the date's short string, which depends on culture.

Five: the clock. The cache entry expires thirty minutes after `DateTime.Now`, which is the server's local time.

Six: log4net, with a log line built by string concatenation.

And seven: `double` for money, which we covered last lesson.

Each of those is a reason it can't be unit tested, and several are reasons it can't run in a container. And the method only handles tiered schedules. Open `legacy/FeeBilling.Core/BlendedFeeCalculator.cs`. The first comment says it was copied from FeeCalculator for the blended schedules in 2013, followed by: keep the two in sync! Put the two files side by side and you'll see two copies of the cache, the AUM lookup and the minimum-fee logic, with different rate logic in the middle. The smell isn't the switch statement that picks a calculator. It's the copy-paste.

## Designing the pure engine

Now the replacement. The approach has two rules. First, capture behaviour before you change anything: that's the golden master from lesson four. Second, write the new implementation beside the old one, not as a refactor of it. The legacy code stays untouched as the measuring stick until cutover.

The shape of the engine is simple. A `FeeRequest` goes in. It contains everything the calculation needs as plain data: the fee schedule, the billable AUM, the billing period, the date the account was opened, and for households, the member accounts and their AUM. A `FeeEngine` calculates. A `FeeResult` comes out: the amount, any household allocations, and a calculation trace. No input or output, no clock, no logging. Same inputs, same output, every time.

Every hidden dependency becomes an explicit input. The AUM that used to come from a stored procedure is now a field on the request. The period that used to be derived from the clock is now a field on the request. That's the core move: make hidden dependencies explicit, and the code becomes testable.

Amounts on the request and the result use a small `Money` value object, which wraps a `decimal` together with a currency. FeeBilling bills in Canadian dollars today, but a value object makes it impossible to add a fee in one currency to an AUM in another by accident, and it gives you one place to put rounding helpers that always take an explicit midpoint mode.

The order of the steps matters as much as the steps themselves, and legacy defines it. First the strategy produces the tier lines. Then the engine applies the day-count basis and rounding according to the policy. Then the minimum fee. And last, pro-rating for accounts opened during the quarter. That last step is easy to miss, because in legacy it isn't in the fee calculator at all. It's in `BillingRunService.CalculateAccountFee`, which calls the calculator and then scales the result. So legacy applies the minimum first, then pro-rates the minimum. The engine has to do the same under the legacy policy, and the trace has to show both steps.

Schedule types are handled with the strategy pattern. The training plan defines an interface called `IFeeStrategy`. Each strategy says which `FeeScheduleType` it handles, and calculates the annual fee for a given AUM and schedule. There's one strategy each for tiered, blended, flat and householded schedules. The engine receives all the strategies from dependency injection and picks one by schedule type.

Is that better than a switch statement? It depends. Use strategies when each variant has real behaviour and tests of its own, which is the case here. A switch is fine for a two-line mapping. The problem in legacy was never the switch; it was duplicating the whole calculator.

Then there's the policy. `BillingPolicy` holds the four numeric dimensions from the last lesson: day-count basis, rounding point, midpoint rounding mode, and allocation method, plus the flag for legacy double arithmetic. There are two presets, `BillingPolicy.Legacy` and `BillingPolicy.Corrected`. Because the shared library targets `netstandard2.0`, a positional record like this needs the `IsExternalInit` polyfill from lesson two. Legacy never constructs these types, so the C# 7.3 restriction doesn't apply.

Now the key insight of the whole design, and a great detail to mention in an interview. Legacy rounds per tier, after dividing by four. So if a strategy returns a single annual number, the engine can never reproduce legacy's rounding, and you'll never reach zero parity differences. The fix is to have strategies return tier lines: each band, its span, and its rate. Then the engine applies the basis and the rounding according to the policy. Per tier for the legacy policy; once at the end for the corrected one.

And "legacy" isn't a single behaviour. The legacy double flag only applies to the paths that legacy computed in `double`, which are the tiered and blended calculators. The household service was `decimal`. The policy has to model the behaviour of each legacy code path.

## Edges versus core

So where do the database, the cache, the logging and the clock go? To the edges. This is sometimes called functional core, imperative shell. The core is pure. The shell, which is the API or the worker, does all the input and output around it.

Picture the billing worker from lesson fifteen. For each chunk of accounts, the shell loads the schedules, the AUM and the household memberships from the database in a few set-based queries. It builds a list of fee requests. It calls the engine for each one, which is pure computation. Then it writes the invoice lines and their traces back in one local transaction. All the input and output happens before and after the engine, never inside it. That's also why the engine is fast: no per-account stored-procedure call in the middle of a calculation.

Loading AUM is the shell's job. Caching, if you need it, is the shell's job. And legacy's cache is a correctness bug, not just a dependency: a thirty-minute, process-wide cache means a recalculation can use stale AUM. Logging is the shell's job too; the engine returns a trace, and the shell decides what to log.

The clock deserves its own point, because "where does the clock go?" is a real interview question. The answer is: not in the domain. The engine takes dates as inputs. If the engine needs today's date, the inputs are incomplete. The application layer asks what today is, using `TimeProvider`, the abstraction built into modern .NET. For `netstandard2.0`, there's a package called `Microsoft.Bcl.TimeProvider`, but the better answer is to keep the clock out of Domain entirely.

Look at `legacy/FeeBilling.Core/QuarterHelper.cs`. Its method `LastQuarterEnd` reads `DateTime.Now` in server local time. Its replacement at the edge takes a `TimeProvider` and a named time zone for the firm. In tests, you use `FakeTimeProvider` from the `Microsoft.Extensions.TimeProvider.Testing` package. That also fixes the legacy test that was ignored because it depends on today's date.

And name the time zone. Here's a concrete example. Set the fake clock to 1 a.m. UTC on September 30, 2026. In UTC, that's September 30, so the last quarter end is September 30. In Toronto, it's still 9 p.m. on September 29, so the last quarter end is June 30. Legacy's `DateTime.Now` meant the on-premises server's time zone. In a container, local time is usually UTC. If you don't name the firm's time zone, moving to containers quietly changes which quarter gets billed near midnight.

## Minimums, households and boundaries

A few domain rules are easy to get wrong in the port, so let's be precise.

The account minimum applies to accounts with their own schedule. The household minimum applies to the household fee before allocation, and the member accounts don't get an account minimum on top. That's what legacy does: `HouseholdFeeService` applies the household's minimum, then the allocator splits the result.

The blended boundary. In the blended calculator, a tier applies if the AUM is strictly greater than the tier's lower bound, or if the lower bound is zero. So an account at exactly $1,000,000 stays at the 1 percent rate; it doesn't reach the 0.75 percent tier. If someone "cleans that up" to greater-than-or-equal, the quarterly fee for that account drops by $625. It might be a bug. It's also what clients were billed. Capture it, raise it, and put any change behind a flag.

And zero-AUM households. Legacy throws. Decide the corrected behaviour with the business, record it in an ADR, and test it.

## The calculation trace

Every result carries a trace of the steps that produced it: which tiers applied, the AUM used, the basis and the factor, the rounding mode and where rounding happened, whether the minimum applied, and any pro-rating. Store it with the invoice line.

Why does this matter so much? Because it's what compliance asks for when a client disputes an invoice. It's what the parity report cites when it categorizes a difference. And it's what the billing review screen in the new front end drills into. The training plan makes it a principle: every calculation is explainable.

## Proving it, and keeping it pure

How do you prove the engine is right? With several kinds of test, each catching different mistakes.

Example tests cover scenarios S1 to S7 with the legacy policy. Since `decimal` can't be an attribute argument, write the expected values as invariant-culture strings and parse them.

Property-based tests, with a library like CsCheck or FsCheck, check rules that must hold for any input: allocations always sum to the household fee, the fee never decreases as AUM increases, and the fee is never below the minimum. The training plan also asks for at least 95 percent branch coverage on the domain.

Then the real acceptance test: the golden master. With `BillingPolicy.Legacy`, zero differences.

Keeping the domain pure over time is a separate problem, because purity erodes one convenient shortcut at a time. Make the wrong thing hard. Keep package references out of the domain project; a simple CI check can fail if `FeeBilling.Domain.csproj` gains one. Add an architecture test, for example with NetArchTest or ArchUnitNET, that fails if the domain depends on Entity Framework Core, ASP.NET Core, `System.Web`, or a logging framework. Check the maintenance status of those libraries before you adopt one. Add a banned-API analyzer for `DateTime.Now`. And write the rule into an ADR, so code review has something to point at.

Finally, where does the engine run? In the new billing worker, and in a what-if preview endpoint in the Billing API. And because Domain targets `netstandard2.0`, legacy could call it too, replacing the statics inside the IIS app. That's a valid option only with zero parity differences, behind a flag, and if the worker migration is far off. Otherwise, it changes production legacy for no gain.

## Traps

Here are the traps.

Refactoring the static classes in place. Build beside them; legacy is the measuring stick.

Strategies that return a single annual number. Then legacy per-tier rounding can't be reproduced.

Putting the cache in the domain.

Injecting `TimeProvider` into the engine "for flexibility". Dates are request data.

Forgetting what local time means in a container.

Fixing the blended boundary while porting.

Applying the minimum at the wrong level.

And ignoring zero-AUM households.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do you make static, untestable business logic testable without changing its behaviour?

[pause 5s]

First I capture its behaviour with a golden master, through a seam, without touching it. Then I write a new pure implementation beside it rather than refactoring it. Every hidden dependency becomes an explicit input: in FeeBilling, the database context from `HttpContext`, the stored-procedure AUM, the cache and the clock. I prove equivalence against the golden master using a legacy policy preset, and only then retire the old code.

**Interviewer:** Where does the clock go?

[pause 5s]

Not in the domain. The engine takes dates as inputs, like the period end and the opened-on date. The application edge asks a `TimeProvider` what today is, in the firm's named time zone, and tests replace it with `FakeTimeProvider`. That matters in containers, where local time is usually UTC, so near midnight you could otherwise bill the wrong quarter.

**Interviewer:** Strategy pattern or a switch statement?

[pause 5s]

Strategies when each variant has real behaviour and its own tests, like tiered, blended, flat and householded schedules, selected by schedule type from dependency injection. A switch is fine for a trivial mapping. The legacy smell wasn't the switch; it was a copy-pasted calculator with a comment saying "keep the two in sync". And the strategies return tier lines, so the engine can reproduce legacy's per-tier rounding.

**Interviewer:** How do you keep a domain layer pure over time?

[pause 5s]

Make the wrong thing hard. No package references in the domain project, an architecture test that fails on dependencies like Entity Framework, ASP.NET Core or logging frameworks, a banned-API analyzer for things like `DateTime.Now`, and an ADR that states the rule so reviews can enforce it.

**Interviewer:** How do you make every calculation auditable?

[pause 5s]

Every result carries a calculation trace: the tiers applied, the AUM used, the basis and factor, the rounding mode and points, whether the minimum applied, and any pro-rating. It's stored with the invoice line. Compliance uses it to answer disputes, the parity report uses it to explain differences, and the review screen shows it.

## Recap

Five things to remember from this lesson.

One: name the hidden dependencies. The legacy calculator depends on an ambient context, EF6, a stored procedure, a cache, the clock, log4net and `double`.

Two: build a pure engine beside the old code. A request in, a result and a trace out, with no input, output or clock.

Three: strategies per schedule type, and an explicit `BillingPolicy`. Strategies return tier lines so legacy rounding can be reproduced.

Four: the clock, cache, database and logging live at the edges, and the time zone is named.

Five: prove it with examples, properties and the golden master, and keep it pure with architecture tests and analyzers.

In the next lesson, we'll move to the web layer: how `Global.asax`, HTTP modules and Web API 2 controllers map onto the ASP.NET Core pipeline, and what breaks silently along the way.
