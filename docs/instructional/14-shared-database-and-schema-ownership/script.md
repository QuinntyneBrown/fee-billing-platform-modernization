# 14 · Shared Database and Schema Ownership

Welcome. This lesson is about a question every long-running migration eventually has to answer: when an old application and a new application share one database, who is allowed to change the schema, and how? It sounds like a process question, but interviewers use it to find out whether you've run a migration in production or only built greenfield systems. By the end of this lesson, you should be able to pick a schema owner and defend it, baseline EF Core migrations against a database that already exists, change a shared schema safely with expand and contract, and explain why migrations shouldn't run when the application starts.

## The questions this lesson answers

Here's what you'll be ready for. Two ORMs share one schema: who owns changes? How do you change a schema that an old app and a new app both use? Should the application run its migrations on startup? How do you know whether a stored procedure is still used? And when do you finally split the shared database?

Keep the thesis from lesson one in mind. You can only prove the new system produces the same invoice as the old one if both systems are reading the same data in the same shape. The schema is the contract between them. So every schema change during a migration is a contract change, and it deserves the same care as a public API change.

## Three models, two databases, zero owners

Let's look at what FeeBilling has today. Open `database/billing/001-schema.sql` and read the header comment. It says the schema was reverse-engineered from production in 2019 and has been hand-maintained since. It says the schema is described by two models: the EF6 EDMX in `legacy/FeeBilling.Data/FeeBilling.edmx`, and the partial EF Core 10 model in `src/FeeBilling.Infrastructure`, which covers Firms, Households and Accounts. And then the line that matters: nobody has decided which of them owns schema changes.

It's actually worse than two. There's a third model. Open `legacy/FeeBilling.Data/Reporting/ReportingEntities.cs`. That's an EF6 Code First context against the second database, FeeBillingReporting. Its comment says it's Code First against an existing database because the EDMX designer kept crashing on the second database, back in 2015. Its static constructor calls `Database.SetInitializer` with null, which tells EF6 never to create or change that database. So it maps tables, but it doesn't own them either.

Now picture a developer who needs a new column tomorrow. Which file do they edit? The SQL script? The EDMX? The EF Core configuration? The reporting context? Today, the honest answer is all of them, by hand, and hope. That's handover issue number four: both the EDMX and the EF Core model describe the same tables, and no one decided who owns schema changes.

## Why sharing was right, and what it costs

Before criticizing the shared database, give the previous team credit. The decision is ADR-0004, in `docs/adr/0004-shared-database-during-transition.md`. They considered a database per service from day one, and rejected it because it requires data synchronization, through change data capture, dual writes or events, before anything useful ships. Instead, the new services map the existing FeeBilling database with EF Core, reverse-engineered with `dotnet ef dbcontext scaffold` and trimmed. Accounts.Api is read-only. That let the first endpoints ship quickly, with no synchronization at all. For the first slice of a strangler fig, that's the right call.

The ADR is also honest about the cost. Two models describe the same tables and can drift. Any schema change can break the legacy app, the new services, or both. Database per service is deferred, not abandoned. And it ends with an open question: who owns schema changes, and which model is the source of truth? This lesson answers that question.

Drift isn't hypothetical in this repository. Open `src/FeeBilling.Infrastructure/Persistence/Configurations/FeeTierConfiguration.cs`. It maps the annual rate with `HasPrecision`, eighteen and two. The real column is a decimal with precision nine and scale six. A rate of 0.0075, which is 0.75 percent, would be written as 0.01. The model and the database disagree, and nothing detects it, because nothing writes fee tiers yet. That's what drift looks like: two descriptions of the same table, each internally consistent, and wrong about each other.

## Choosing an owner

There are three realistic options for who owns the schema.

Option one: a DBA-owned set of SQL scripts, managed with a tool like DbUp or a SQL Server Data Tools project. That's legitimate where a strong DBA team exists.

Option two: the EDMX. It's tempting because it's the oldest model. But EF6 Database-First never owned the schema. The database was the source, and the EDMX was generated from it. You can see that in `legacy/FeeBilling.Data/Model/FeeBilling.Context.cs`: `OnModelCreating` throws `UnintentionalCodeFirstException`. Database-First is a reader of the schema, not a writer.

Option three, and the recommendation: EF Core migrations become the source of truth. The reasons are practical. The schema change lives in the same pull request as the code that needs it. It's reviewed like any other code. It's versioned with the application. And it's one tool for all new work. The EDMX becomes read-only: when the schema changes, legacy regenerates its model from the database, and nobody edits it by hand.

Then add the rule that makes this safe while legacy is still running. Every change must stay backward compatible with the legacy application until legacy is retired. EF Core owns the schema, but legacy still has a veto.

Write all of that down as a new ADR that answers ADR-0004's open question: the owner, the tool, the review rule, how migrations are deployed, and the expand and contract rule. Don't leave it in a chat thread.

## Baselining a database that already exists

Here's the practical problem. EF Core migrations assume they built the database. Ours was built by SQL scripts years ago. How do you start using migrations without recreating anything?

You create a baseline migration and empty it. You run `dotnet ef migrations add Baseline` against the Infrastructure project, with Accounts.Api as the startup project. EF Core generates a migration and a model snapshot. Then you delete the body of the Up and Down methods, so the migration does nothing, and you leave a comment saying the schema already exists and this migration only records that fact.

When you apply it to an existing environment, two things happen. EF Core creates the migrations history table, named `__EFMigrationsHistory`, if it doesn't exist, and inserts one row for the baseline. No other DDL runs. From then on, every new migration is a real, reviewed change on top of the as-found schema.

For fresh development and test databases, `tools/FeeBilling.DbInit` builds the as-found schema from the SQL scripts, and then migrations run on top. The integration tests already do the right thing: `AccountsApiFixture` builds its SQL Server container from the same scripts, through `SqlScriptRunner`, so tests exercise the real schema, not EF Core's idea of it.

Now the trap, and it's a good one to mention in an interview. EF Core never compares your model with the live database. It compares the model with its snapshot. Today the snapshot only knows about Firms, Accounts and Households. When someone later wires in the FeeSchedule and FeeTier configurations, which are scaffolded but not applied in `FeeBillingDbContext.cs`, the next migration will contain a create-table for FeeTiers, a table that already exists. Either map every table you intend to own before you generate the baseline, or edit that later migration down to nothing and explain why in the pull request.

The same blind spot explains why migrations can't catch the rate precision bug. The model and the snapshot agree with each other. Neither looks at the column. The fix is a drift test: an integration test that reads column types from `INFORMATION_SCHEMA.COLUMNS` in the real database and compares them with EF Core's metadata for every mapped property. On this repository, that test fails on the annual rate until the precision is fixed to nine comma six. That's exactly what you want.

## Expand and contract

Now, how do you change a schema that two applications use? The technique is called expand and contract, or parallel change. You never change a shape in place. You add the new shape next to the old one, move everyone across, and only then remove the old shape.

It has five steps. Expand: add the new column or table, nullable, so nothing that exists today notices. Backfill: copy existing data into the new shape, in batches. Dual write: keep both shapes up to date, from the application or with a trigger. Switch reads: point the new code at the new shape. Contract: remove the old shape, but only once no consumer needs it, which during a strangler fig usually means after legacy is retired.

Let's do two worked examples from FeeBilling.

The first is additive and safe. The Billing API needs an idempotency key on billing runs, so a double-click doesn't create two runs. You add a nullable varchar column called `IdempotencyKey` to `BillingRunQueue`, and a unique index on it filtered to rows where the key isn't null. Legacy doesn't notice. EF6 Database-First only selects the columns in its storage model, so an extra nullable column is invisible to it. The stored procedure `usp_EnqueueBillingRun` keeps inserting rows without a key, and the filtered index ignores them. There's no contract step here: the column stays. One caveat to check on the real server before shipping: changing data in a table that has a filtered index requires the quoted identifier setting to be on, and a stored procedure keeps the setting it was created with. So check that procedure's setting first.

The second example needs all five steps. `Accounts.LastValuedAt` is a datetime2 column holding UTC by convention. The schema comment says it's written by the nightly valuation job, in UTC. But the column type doesn't say UTC, and that's the root of handover issue number three, where dates come back with an unspecified kind and the browser treats them as local time. You'd like a date time offset column instead. Changing the type in place would break the EDMX, which expects a plain DateTime. So: expand, by adding a new nullable date time offset column, with UTC in its name. Backfill it from the old column in batches. Dual write, by having the valuation job write both columns. Switch reads, by mapping the new column in EF Core so the new APIs read it. Legacy notices none of this. And contract, by dropping the old column only after legacy and the EDMX are gone, which belongs on the decommissioning checklist.

Two rules fall out of this. Never rename or drop in one step. EF Core will happily generate a valid rename-column migration, and legacy breaks on its next query. And never add a NOT NULL column without a default, because legacy inserts don't know the column exists, and they fail.

Watch for one more subtle case: a schema change that's really a behaviour change. Suppose you add a unique index on `BillingRunQueue` across firm and period end, to stop duplicate runs. That might be the right thing to do, but now legacy's double-click throws an error instead of creating a second run. You've changed legacy behaviour through the database, so it needs the same sign-off as a code change. And if duplicate rows already exist, the index won't even create.

## Deploying migrations

How do migrations reach production? The easy answer is to call `Database.Migrate()` when the application starts. In a multi-instance or regulated deployment, don't.

The application's SQL login would need permission to change the schema, which breaks least privilege. Several instances starting at once race each other. Newer EF Core versions take a lock to reduce that, but check the behaviour for your version. A failed migration takes the application down in the middle of a rollout. And nobody reviewed the SQL that ran against production.

Instead, generate the SQL and review it. `dotnet ef migrations script` with the idempotent flag produces a script that checks the history table before each step, so it's safe to run against any environment, whatever state it's in. Or build a migration bundle with `dotnet ef migrations bundle`, which is a self-contained executable that applies pending migrations. Either way, a pipeline step runs it with an elevated login, before the new application version is deployed, and the application itself runs with a login that can read and write data but can't change the schema.

DbInit, by the way, is for development and test only. It isn't a production deployment tool.

## Stored procedures and the reporting database

Ownership also covers the fourteen stored procedures in `database/billing/002-stored-procedures.sql`. Its header comment says some are hot paths, called once per account per billing run, some are called from one screen, one or two may not be called at all any more, and nobody has checked which is which.

Start with the repository. A `git grep` for names beginning with usp underscore, across the legacy and src folders, finds six of the fourteen: `usp_GetBillableAum`, `usp_EnqueueBillingRun`, `usp_GetInvoicesForRun`, `usp_ReconcileFeeDebits`, `usp_UpsertPosition` and `usp_ValidateUser`. That's useful, but it isn't proof that the other eight are unused. The repository only shows what the repository calls. Reports, SQL Agent jobs, the month-end export tool and the FeedProcessor scheduled task on APP01 aren't in this repository.

So ask the database. The dynamic management view `sys.dm_exec_procedure_stats` shows execution counts and the last execution time since the plan was cached. Query Store gives you history that survives a restart. Or run an Extended Events session. And observe for a full billing cycle, because quarter-end jobs only run once a quarter. Deleting a procedure after watching for a week is how you find out, on the last day of the quarter, that it wasn't unused.

Then there's the reporting database. FeeBillingReporting is a second ownership problem, and the previous team was reassigned to the Reporting platform, so the people who understand it are elsewhere. Today it's written by the billing runner inside the same transaction scope as the invoices, which is why production needs MSDTC. That's the subject of lesson sixteen.

And finally, database per service. Deferred is not abandoned. The path there is gradual: first a table-by-table ownership map, then a schema per service with a separate SQL login per service, then views as contracts where one service needs another's data, and finally change data capture or events to feed copies. You split the database when ownership is clear and no cross-service joins remain, not before.

## Traps

A few traps to call out.

Assuming the ORM will figure it out. Migrations compare the model with the snapshot, never with the live database, so existing drift like the rate precision stays invisible.

Mapping an existing table after the baseline. The next migration tries to create it.

Renaming or dropping in one step. Destructive changes wait for the contract phase.

Adding a NOT NULL column without a default. Legacy inserts fail.

Treating a schema change as purely technical when it changes legacy behaviour, like a unique index that makes the double-click throw.

Running migrations at startup. The permission and review problems remain even if concurrency is handled.

Deleting procedures based on a repository search. Observe for a full quarter.

And forgetting the reporting database. It's a third model with its own owner question.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** Two ORMs share one schema. Who owns changes?

[pause 5s]

Exactly one owner, and it's written down in an ADR. I'd make EF Core migrations the source of truth, because the schema change then lives in the same pull request as the code that needs it, it's reviewed, and it's versioned. The EF6 EDMX becomes read-only and is regenerated from the database. The condition is that every change stays backward compatible with the legacy app until legacy is retired. I'd also add a drift test that compares the EF model with the real column types, because migrations never look at the live database.

**Interviewer:** How do you change a schema that an old app and a new app both use?

[pause 5s]

Expand and contract. Add the new shape as a nullable column or a new table, backfill it in batches, dual-write or sync while both shapes exist, switch the new code's reads, and only drop the old shape once nothing needs it, which usually means after legacy is gone. Never rename or drop in one step, and never add a NOT NULL column without a default, because legacy inserts don't know it exists. Additive, nullable changes are invisible to EF6 Database-First.

**Interviewer:** Should the application run migrations on startup?

[pause 5s]

Not in a multi-instance or regulated deployment. The application would need rights to change the schema, instances race each other, a failed migration takes the app down mid-rollout, and nobody reviewed the SQL. I'd generate an idempotent script or a migration bundle, review it in the pull request, and run it as a pipeline step with an elevated login, while the app itself runs with least privilege.

**Interviewer:** How do you know whether a stored procedure is still used?

[pause 5s]

The repository only shows what the repository calls. In FeeBilling, six of the fourteen procedures are referenced from C#, but reports, agent jobs and scheduled tasks live elsewhere. So I'd check the procedure stats view and Query Store, or run an Extended Events session, for a full billing cycle, because quarter-end jobs only run once a quarter.

**Interviewer:** When do you split the shared database?

[pause 5s]

When ownership is clear table by table and no cross-service joins remain. I'd get there gradually: an ownership map, a schema and a SQL login per service, views as contracts between services, then change data capture or events to feed copies. Sharing the database was right for the first slices, because it avoided synchronization. It's deferred, not abandoned.

## Recap

Five things to remember from this lesson.

One: FeeBilling's schema is described by three models, the EDMX, the partial EF Core model and the reporting context, and nobody owns it. ADR-0004's open question needs an answer in a new ADR.

Two: make EF Core migrations the owner, keep the EDMX read-only, and require backward compatibility with legacy until it's retired.

Three: baseline an existing database with an empty migration. It only adds a row to the history table. Remember that EF compares the model with its snapshot, never the live database, so add a drift test.

Four: expand, backfill, dual write, switch reads, contract. Never rename or drop in one step, and never add NOT NULL without a default.

Five: deploy migrations as a reviewed script or bundle in the pipeline, not from application startup, and prove a stored procedure is unused by watching a full quarter, not by searching the repository.

In the next lesson, we'll replace the Windows Service that runs billing with a .NET Worker Service, and make a billing run resumable instead of all or nothing.
