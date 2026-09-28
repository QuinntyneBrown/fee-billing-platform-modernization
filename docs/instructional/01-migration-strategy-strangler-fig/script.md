# 01 · Migration Strategy: Strangler Fig vs Big Bang

Welcome. This lesson is about the first question you'll almost certainly get in a modernization interview, and the one that frames every other answer you give. By the end of it, you should be able to explain how you'd migrate a large .NET Framework application to modern .NET, why you'd do it incrementally, what order you'd do it in, and how you'd know each step was finished. We'll use the FeeBilling repository as the worked example the whole way through, so every claim is backed by something you can open and point at.

## The questions this lesson answers

Here are the questions this lesson prepares you for. How would you approach modernizing a large .NET Framework application? Big bang or incremental? What would you migrate first, and why? How do you know a migrated slice is finished? And the one that tests judgment rather than knowledge: you inherit a migration that stalled at thirty percent, so what do you do in your first week?

Keep one sentence in your head while you listen, because the rest of the series hangs off it. In fee billing, the migration is only done when you can prove the new system produces the same invoice, to the cent, for every account, and explain every place it intentionally doesn't. That's the thesis. Strategy, testing, data, cutover: all of it exists to make that sentence true.

## Big bang versus strangler fig

Let's start with the two broad strategies.

A big-bang rewrite means you build a new system alongside the old one, and on some future date you switch everyone over at once. It's attractive because it looks clean. You don't have to live with the old code, and you don't run two stacks at the same time.

It has three failure modes, and you should be able to name all three. First, value arrives late. The business gets nothing until the very end, which might be eighteen months away, and priorities change long before then. Second, risk arrives all at once. Every hard problem, from authentication to data migration to that one report nobody documented, surfaces in the same few weeks before cutover. Third, rollback is all or nothing. If something goes wrong after the switch, you can only go back by abandoning everything.

Big bang is a reasonable choice in a narrow set of cases: the application is small, it has good automated tests, and it doesn't share state such as a database with other systems. FeeBilling is none of those things.

The alternative is the strangler fig pattern, named by Martin Fowler after a vine that grows around a tree until it replaces it. You put a facade in front of the legacy application, so every request goes through one front door. Then you move one route, or one component, at a time to a new service. Anything you haven't migrated yet falls through to the legacy system unchanged. Each step can be deployed on its own and rolled back on its own.

The cost of the strangler fig is real, and saying so out loud is what makes your answer credible. For a long time, you run two stacks, two object-relational mappers, and one shared database, and you have to keep them consistent. You also need transitional plumbing, like shared authentication, that you'll throw away later. For a system that bills money, that cost is worth paying, because the alternative is betting the company's invoices on a single cutover weekend.

## What FeeBilling looks like today

Now let's ground this in the repository. FeeBilling calculates and collects advisory fees for about forty wealth-management firms, roughly three hundred and fifty thousand accounts. It was built between 2011 and 2016 on .NET Framework 4.7.2, and it has been maintained rather than evolved since.

Open `legacy/FeeBilling.sln` and you'll find seven projects. The interesting thing, and the thing to say in an interview, is that each one is blocked from modern .NET for a different reason. There is no single upgrade step.

`FeeBilling.Web` is ASP.NET MVC 5 and Web API 2 on `System.Web`, which doesn't exist in modern .NET. It uses Forms authentication, the Unity container, and `HttpRuntime.Cache`, and its JSON contracts are PascalCase because of Newtonsoft's defaults. The AngularJS front end depends on those exact shapes.

`FeeBilling.Core` holds the business logic. It's a set of static classes, and the fee calculator gets its database context from `DbContextFactory.Current`, which reads `HttpContext.Current.Items`. It calculates money with `double`, and it hard-codes a divide by four for the quarterly fee. This is the highest-risk code in the system, because it produces the invoices.

`FeeBilling.Data` is Entity Framework 6, database-first, with an EDMX model, fourteen stored procedures, and some ADO.NET data sets for exports.

`FeeBilling.BillingRunner` is a Windows Service that polls a queue table every thirty seconds. It wraps the whole billing run in a `TransactionScope` that spans two databases, which means it depends on MSDTC, the distributed transaction coordinator. The comment in `BillingRunnerService.cs` sums it up: a crash at account forty thousand rolls the whole run back, and it starts over.

`FeeBilling.Ingestion.Wcf` is a WCF service that receives custodian position files over SOAP. It stages them with `BinaryFormatter`, which was removed in .NET 9, and it parses numbers and dates with the server's culture. Three client firms run a feed agent that can't be upgraded for about six months, so the SOAP contract is frozen.

`FeeBilling.Invoicing` draws PDF invoices with `System.Drawing`, which is Windows-only in modern .NET.

And `FeeBilling.Tests` is an MSTest suite with sixty-one tests: thirty pass, nine fail, and twenty-two are ignored. Nobody trusts it, and the CI workflow in `.github/workflows/ci.yml` runs it with continue-on-error set to true, so it doesn't gate anything.

A good first-week habit is to build this inventory from the code, not from memory. A single `git grep` across the legacy folder for `HttpContext.Current`, `ConfigurationManager`, `BinaryFormatter`, `TransactionScope`, `System.Drawing`, `Encoding.Default` and `HttpRuntime.Cache` gives you a map of every place that will break or behave differently. Turn that into a one-page risk register: the component, why it can't move as-is, its modern target, its risk, and why. For FeeBilling, the three high-risk rows are the fee engine, the billing runner, and custodian ingestion. The web layer, the data layer and the front end are medium. PDF invoicing is low to medium: it's client-visible, but it doesn't calculate money.

## How the strangler works in this repository

The previous team chose the strangler fig, and it's recorded in `docs/adr/0001-strangler-fig-migration.md`. Read the consequences section of that ADR. It lists the negative consequences honestly: two stacks, two ORMs, a shared database, and transitional authentication that couples new services to legacy uptime. That honesty is what a good ADR looks like.

The facade is `src/FeeBilling.Gateway`, an ASP.NET Core application hosting YARP, Microsoft's reverse proxy library. Open its `appsettings.json`. There are three routes. The accounts route and the households route send `/api/accounts` and `/api/households` to a cluster called modern-accounts, which is the new `FeeBilling.Accounts.Api` on port 5101. The third route is the fallback. It matches every path with a catch-all pattern and sends it to the legacy IIS cluster on port 8080. It has an Order of one thousand, which means it's evaluated last, so any more specific route wins.

That configuration is the whole strangler in miniature. Migrating a route means adding a route entry and deploying. Rolling back means removing it. The legacy application doesn't change at all.

Now look at `legacy/FeeBilling.Web/Controllers/Api/AccountsController.cs`. The first comment says: migrated, now served by FeeBilling.Accounts.Api via the YARP gateway, kept for rollback, do not add features here. That's a sensible transitional state, but it's also a risk. As long as that controller exists, a routing mistake silently serves the old behaviour. Part of finishing a slice is deleting the legacy path.

Two more pieces of transitional plumbing are worth knowing. First, the new services share the existing FeeBilling database. That decision is ADR-0004, and it let the team ship the first endpoints without any data synchronization. Second, authentication still belongs to the legacy app. Accounts.Api resolves the user by calling back into the legacy application on every request, using the `SystemWebAdapters` remote authentication feature. The handover notes the cost: if IIS is down, Accounts.Api returns a 500 on everything, including its health endpoint. So the strangler has reduced risk in one place and introduced coupling in another. Being able to say that is a sign you've actually run one of these.

## Sequencing the migration

Order matters more than technology in this answer. Here's the sequence for FeeBilling, and more importantly, the reason for each step.

First, extract shared types into a library both sides can use. That's already done: `src/FeeBilling.Domain` targets `netstandard2.0`, so the .NET Framework code and the new .NET 10 services can both reference it.

Second, put the gateway in front of everything. Also done.

Third, migrate something low-risk and read-mostly, to prove the whole pipeline end to end: routing, authentication, data access, CI, and deployment. The previous team picked Accounts and Households, which is exactly right. They're on a real traffic path, but they don't calculate money and they don't write.

Fourth, and this is the step the previous team skipped, build a golden master. Before you touch the fee engine, you capture exactly what the legacy engine produces for a known set of inputs, and you commit that output. That's work package one, and it's the subject of lesson four. Handover issue number five says it plainly: no parity tests exist for anything fee-related.

Only then do you port the fee engine into a pure domain, then build the Billing API, then replace the billing runner with a worker, then move ingestion, and then work through data ownership, configuration, authentication and the front end. Cutover and decommissioning come last.

There's one rule that runs through the whole sequence, and it's the most important sentence in this section. A migration change and a behaviour change must never ship together. If the new fee engine fixes a rounding bug at the same time as it moves to .NET 10, and a client's invoice changes by a cent, you can't tell which change caused it. So you reproduce legacy behaviour first, prove it, and then fix things separately, behind a flag, with sign-off.

## Definition of done, and why writes are the hard part

How do you know a slice is finished? Give a checklist, not a feeling. Traffic is routed to the new service. Contract tests prove it returns the same shapes the old endpoint did, and parity tests prove it produces the same results. It's observable: logs, traces and metrics exist and someone is looking at them. Rollback has actually been exercised, not just described. And the legacy code path is deleted, or at least frozen with a date for deletion.

Then add the nuance that most candidates miss. Reads are trivially reversible. If the new accounts endpoint misbehaves, you remove its route and traffic goes back to legacy, and nothing is lost. Writes are not reversible that way. If the new Billing API writes rows, and you roll back to legacy, the legacy code has to be able to read what the new code wrote. That means every schema change must stay backward compatible while legacy is still running. The technique is called expand and contract: you add the new shape alongside the old one, migrate data and code across, and only remove the old shape once nothing uses it.

Rollback also works best when it's granular. In FeeBilling, the natural unit is the firm. If you cut over firm by firm behind a feature flag, a problem with one firm's invoices doesn't force you to roll back all forty.

## Reviewing inherited decisions

When you inherit a stalled migration, the documents are a starting point, not the truth. Verify them against the code and the calendar.

Take `docs/adr/0002-target-dotnet-version.md`. It says new services target .NET 8, the long-term support release. As of September 2026, .NET 8 reaches end of support on November 10, 2026, only weeks away. .NET 10 is the current long-term support release, supported until November 2028. So the decision was reasonable when it was made and is wrong now. The right move isn't to edit the old ADR in place. You write a new ADR that supersedes it, and you mark the old one as superseded, so the history of the decision survives.

Take ADR-0004, the shared database. It ends with an open question: who owns schema changes, and which model is the source of truth? Both the EDMX and the new EF Core model describe the same tables. That's handover issue number four, and until someone answers it, every schema change is a coordination problem.

And look at the handover's known issues with a skeptical eye. Issue number two says the EF Core mapping for a fee tier's annual rate uses a precision of eighteen comma two, while the database column is nine comma six. A rate like 0.0075 would be stored as 0.01. Nobody noticed because nothing writes tiers yet. That's a latent bug waiting for the first write, and it's exactly the kind of thing a parity harness and a round-trip test would catch.

So, your first week: read the ADRs and the handover, verify them against the code, build the risk register, start the golden master, and deal with the time-critical item, which is the .NET 8 end-of-support date.

## Traps

A few traps separate people who've done this from people who've read about it.

Answering with a technology list. YARP, CoreWCF and EF Core are tools, not a strategy. The strategy is the order, the risk reduction and the reversibility.

Starting with the fee engine because it's the most important part. It's the part you can least afford to get wrong, which is exactly why it moves after its behaviour has been captured.

Refactoring legacy code to make it testable before capturing its output. If you change the code first, you've changed the thing you were trying to measure.

Trusting the handover. ADR-0002 is out of date, and the rate precision bug is sitting there unnoticed.

Forgetting that writes break rollback. Routing back to legacy doesn't undo rows the new service already wrote in a shape legacy can't read.

And never deleting the legacy path. Until the old controller is gone, you have two sources of truth.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How would you approach modernizing a large .NET Framework application?

[pause 5s]

I'd start with an inventory: dependencies, how coupled the code is to `System.Web`, WCF services, background jobs, and Framework-only packages, turned into a risk register. I'd retarget shared libraries to `netstandard2.0` so old and new code can share them. Then I'd put a reverse proxy like YARP in front of everything and migrate route by route, starting with something low-risk and read-mostly to prove the pipeline. For the high-risk logic, which in a billing system is the fee calculation, I'd capture its current behaviour with characterization tests before moving it. Every step is independently deployable and reversible, and the final cutover happens tenant by tenant behind feature flags, after a period of shadow runs.

**Interviewer:** Big bang or incremental?

[pause 5s]

Incremental, unless the application is small, well-tested and doesn't share state. A big bang delivers no value until the end, surfaces every hard problem at once, and can only be rolled back all at once. The honest cost of incremental is running two stacks, a shared database and some transitional plumbing for a while. For a system that bills money, that's the right trade.

**Interviewer:** What would you migrate first, and why?

[pause 5s]

Something read-mostly and low-risk that's still on a real traffic path, so it proves routing, authentication, data access, CI and deployment end to end. In FeeBilling, that was Accounts and Households. I would not start with the fee engine. It moves after a golden master has captured exactly what the legacy engine produces.

**Interviewer:** How do you know a migrated slice is finished?

[pause 5s]

Traffic is routed to it, contract and parity tests pass, it's observable, rollback has actually been exercised, and the legacy code path is deleted or frozen with a removal date. And for anything that writes, legacy must still be able to read what the new service wrote, so schema changes follow expand and contract until legacy is retired.

**Interviewer:** You inherit a migration that stalled at thirty percent. What do you do in your first week?

[pause 5s]

Read the ADRs and the handover, and then verify them against the code, because they drift. In this case, the target-version ADR says .NET 8, which reaches end of support on November 10, 2026, so I'd write an ADR superseding it with .NET 10. I'd build a risk register from the code, start the golden-master harness for the fee engine, since that's the gap blocking everything valuable, and write down the open questions, like who owns the database schema, with owners and dates.

## Recap

Five things to remember from this lesson.

One: the strategy answer is a sequence, not a technology list. Inventory, shared libraries, facade, low-risk slice, golden master, then the high-risk components, then cutover.

Two: big bang fails in three ways: value arrives late, risk arrives all at once, and rollback is all or nothing. The strangler fig's cost is two stacks and a shared database for a while, and you should say so.

Three: in FeeBilling, the gateway's fallback route with an Order of one thousand is the strangler in miniature. Migrating is adding a route; rolling back is removing it.

Four: never ship a migration change and a behaviour change together. Reproduce first, prove it, then fix behind a flag.

Five: reads roll back for free; writes don't. Expand and contract keeps rollback possible while legacy lives.

In the next lesson, we'll look at target frameworks and shared libraries: why .NET 10, and how a `netstandard2.0` library lets the old and new code share types during the transition.
