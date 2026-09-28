# 13 · EF6 to EF Core

Welcome. "What changed between Entity Framework 6 and EF Core?" sounds like a trivia question, but interviewers ask it because the differences cause real production bugs during a migration: queries that quietly load ten thousand rows, precision lost on write, and concurrency bugs that EF6 tolerated and EF Core rejects. FeeBilling has an example of each. This lesson walks through the model, loading, querying, writing and testing differences, with FeeBilling code for every one, so you can answer with specifics instead of a feature list.

## The questions this lesson answers

Here are the questions this lesson prepares you for. What changed between EF6 and EF Core that bites during a migration? How do you find N+1 queries? Why not use the InMemory provider for tests? Would you migrate EF6 to EF Core in the same step as moving to .NET 10? And when would you keep a stored procedure?

Start with the bug that's already waiting in the new code, because it makes every other point concrete.

## The precision bug waiting for its first write

Open `src/FeeBilling.Infrastructure/Persistence/Configurations/FeeTierConfiguration.cs`. The comment says it was scaffolded for Billing.Api and isn't applied yet. The last line of the configuration maps `AnnualRate` with `HasPrecision(18, 2)`: eighteen digits, two after the decimal point.

Now open the schema, `database/billing/001-schema.sql`. The `FeeTiers` table defines `AnnualRate` as decimal nine comma six, with a comment: 0.007500 equals 0.75 percent. Six digits after the point. And the domain entity, `src/FeeBilling.Domain/Entities/FeeTier.cs`, documents it too: annual rate as a fraction, 0.0075 equals 0.75 percent, stored as decimal nine comma six.

So the configuration disagrees with the database it claims to have been scaffolded from. What happens? Reads of existing data mostly look fine. But the first time the new API writes a tier, EF Core sends a parameter declared with the precision you configured, two decimal places. A rate of 0.0075 becomes 0.01. A 0.75 percent tier becomes 1 percent, on every account billed on that schedule, from that moment on. That's handover issue number two, and the handover says why nobody noticed: nothing writes yet. And look at `FeeBillingDbContext`: the lines that would apply the fee schedule and fee tier configurations are commented out, waiting for Billing.Api.

The fix has an order. First correct the mapping to `HasPrecision(9, 6)`. Then wire the configuration into the context. Then prove it with a round-trip test against a real SQL Server: write a tier with a rate of 0.0075, read it back with a fresh context, and assert it's still 0.0075. While you're there, test what happens to a value with more decimals than the column allows. The schedule editor converts percentages in JavaScript, so a 0.35 percent rate arrives as 0.0034999999999999996. Whether that's rounded or truncated on the way in should be decided and tested, not assumed.

And the general lesson: when a configuration says it was scaffolded, and you have any doubt, scaffold the table again and diff. The database is the truth.

## Two models of one database

Now the bigger picture. The legacy model is an EDMX file, `legacy/FeeBilling.Data/FeeBilling.edmx`. Database-first EF6 keeps three models inside it: the conceptual model, the storage model, and the mapping between them. At build time those are split into three embedded resources, which is what the metadata part of the legacy connection string points at. The entity classes under `legacy/FeeBilling.Data/Model` were generated from T4 templates, and the generated context class overrides `OnModelCreating` to throw an `UnintentionalCodeFirstException`, a guard against accidentally using the model code-first.

EF Core has no EDMX. The model is plain entity classes plus configuration, usually one `IEntityTypeConfiguration<T>` class per entity, applied in `OnModelCreating`. You get there by reverse-engineering the live database with `dotnet ef dbcontext scaffold`, and then trimming to what each service needs. That's what the previous team did for firms, households and accounts.

There's an option people forget: EF6, from version 6.3 on, runs on modern .NET. So "new runtime, same ORM" is a legitimate first step for code that isn't being rewritten. It separates two risky changes, moving to .NET 10 and moving to EF Core, which is the same principle as separating a migration change from a behaviour change. In FeeBilling the new services use EF Core from the start, because they're new code. But if you were lifting the legacy data layer as-is, moving runtimes first is a defensible plan.

Type mappings are where the models drift. The EDMX knew every column's type because it was generated from the database. An EF Core configuration only knows what you tell it, and the defaults aren't the database's. Look at `AccountConfiguration.cs` for the careful version: `IsUnicode(false)` for varchar columns, `IsFixedLength` for the three-character currency code, a column type of date for `OpenedOn`, precision zero for `LastValuedAt`, and the column type datetime for `CreatedOn`. That's the level of care every configuration needs.

Dates are a good example of why. EF Core maps a C# `DateTime` to SQL Server's datetime2 type by default. FeeBilling's schema uses the older datetime type for columns like `CreatedOn` and the invoice date. If the configuration doesn't say so, EF Core sends datetime2 parameters to a datetime column, and comparisons and rounding behave slightly differently, because the two types have different precision. That's why `CreatedOn` is mapped with an explicit column type. And whichever type the column is, SQL Server stores no time zone, so values come back with an unspecified kind, which is how lesson eleven's off-by-one date happened. A value converter that marks UTC columns as UTC fixes that in one place.

## Lazy loading and N+1 queries

The second big difference is loading. The EDMX has lazy loading switched on: the entity container carries a lazy-loading-enabled annotation set to true, and the generated navigation properties are virtual. So in the legacy code, touching a navigation property quietly runs a query.

The legacy controllers depend on that everywhere. In `AccountsController`, the list action calls `ToList` and then maps each account, with the comment: household and fee schedule lazy-loaded per account. The positions action is worse: the comment says it loads every position for the account, then filters in memory. In `BillingController`, the runs list counts and sums each run's invoices, with the comment: lazy load per run. And in `InvoicesController`, reading each invoice's account number is a lazy load per invoice, forty thousand of them for a big firm.

That pattern is called N+1: one query for the list, then one more query for each row. It's invisible in development with thirteen accounts, and it's a production incident with forty thousand.

EF Core has no lazy loading unless you opt in, by adding the `Microsoft.EntityFrameworkCore.Proxies` package and calling `UseLazyLoadingProxies`. Don't. Turning it back on to make the port compile reproduces every N+1 in the legacy code. Instead, turn each case into a deliberate load. Usually that's a projection: select exactly the columns the response needs, including the related ones, and EF Core writes one query with a join. Use `Include` when you really need the whole related entity. And when a query includes several collections, consider `AsSplitQuery`, which loads each collection in its own query instead of multiplying rows in one giant join, a problem called cartesian explosion.

Split queries have a trade-off of their own. They're several round trips instead of one, and unless you wrap them in a transaction, the data can change between them, so a parent and its children can come from slightly different moments. For a billing run that's being written while you read it, decide whether that matters.

The legacy string form, include with a dotted path like FeeSchedule dot Tiers, still works in EF Core, but the lambda form with `Include` and `ThenInclude` is safe to refactor, because renaming a property breaks the build instead of the query.

How do you find N+1s? Log SQL: the `Microsoft.EntityFrameworkCore.Database.Command` category at Information level shows every command. Accounts.Api sets it to Warning in `appsettings.json`, so turn it up locally. In integration tests, a command interceptor can count commands per request and fail when a list endpoint issues more than a handful. In shared environments, use Query Store or your tracing tool. And in code review, look for navigation access inside loops, or a select after `ToList`.

## Query translation, tracking and concurrency

Translation rules changed across versions, and it's worth knowing the history. EF6 throws a not-supported exception when it can't translate a method to SQL. EF Core 1 and 2 did something dangerous: they silently evaluated untranslatable parts on the client, which could mean pulling a whole table into memory. EF Core 3.0 and later throw instead, with one exception: client code is still allowed in the final projection, the last `Select`, where it runs per row after the data comes back. That's convenient, and it can hide per-row work.

The same LINQ can also translate differently between the two ORMs: string comparisons, date functions and group-by are the usual suspects. Only integration tests against the real engine catch those.

For reads, use `AsNoTracking`, which skips change tracking. Accounts.Api already does this in every query in `AccountsEndpoints.cs`.

Concurrency is the one that surprises people. A database context isn't thread-safe in either ORM. The difference is that EF Core detects two operations on one context at the same time and throws an `InvalidOperationException`, saying a second operation was started on this context before a previous operation completed. EF6 often didn't notice. The legacy billing runner shares one `FeeBillingEntities` across `Parallel.ForEach`, and it "works." Port that to EF Core and it throws, which is good news: the exception is finding a bug the legacy code always had. The fix is one context per unit of work, from lesson eight, and the worker design in lesson fifteen.

## Writes, raw SQL and stored procedures

One legacy write deserves a story. In `BillingController`, the approve action updates invoice status with raw SQL, and the comment explains why: FB-311, invoice status is marked store-generated computed in the EDMX, so EF silently ignores changes to it. Open the EDMX and you'll find that computed annotation on the invoice's status property. But in the database, the status column just has a default value of draft. Nothing computes it. The flag was hand-made in the EDMX, and it caused a workaround in the code. When you regenerate a model from the database, flags like that vanish, and behaviour changes. Audit the EDMX for annotations the schema doesn't justify.

In EF Core, the approve action becomes a set-based update in one statement with `ExecuteUpdateAsync`: filter invoices by run and draft status, and set status, approved-by and approved-on. One caution: legacy sets the approval time with the database's get-date function, which returns the server's local time. If the new code writes UTC, that's a behaviour change, so decide it deliberately.

Raw SQL maps across like this. EF6's `Database.SqlQuery` becomes EF Core's `Database.SqlQuery`, which supports scalar and unmapped types since EF Core 8. `FromSql` returns entity types, and `ExecuteSql` runs commands. Interpolated values become parameters, which is what protects you from SQL injection. And you can't compose more LINQ on top of a stored procedure call: EF can't wrap an exec in a subquery, so materialize the results first.

Which stored procedures do you keep? The schema file lists fourteen, with a header comment admitting some are hot paths, some are called from one screen, and some may not be called at all. A `git grep` across the legacy and new code finds only six of them called from C# in this repository. The others might be called by reports or scheduled jobs outside the repo, so check the server's execution statistics before deleting anything. Keep the measured hot paths and the set-based ones: `usp_GetBillableAum` runs once per account per billing run. Port the rest to LINQ with tests, and delete the dead ones. The ADO.NET data set exports become projections to typed rows.

## Testing that catches mapping bugs

Finally, testing, because it's where most of these bugs are either caught or waved through.

The EF Core InMemory provider isn't relational. It doesn't translate to SQL, so translation bugs pass. It doesn't enforce constraints, column types or precision, so the eighteen comma two mapping passes happily. It doesn't do real transactions. SQLite is closer, but its types and functions differ from SQL Server's. The only test that catches the precision bug is one against SQL Server itself.

The repo already has the pattern. `AccountsApiFixture` starts SQL Server in a container with Testcontainers, applies the same database scripts used for local development, and points the API at it. A round-trip test for fee tiers would use the same fixture, with a small helper to create a context against the container's connection string.

## Traps

The traps to call out.

Trusting a scaffolded configuration. `FeeTierConfiguration` says it was scaffolded and disagrees with the database.

Precision bugs that hide until the first write.

Turning lazy loading back on to make the port compile.

Unit-testing queries against InMemory.

Composing LINQ over a stored procedure call.

Sharing a context across threads.

And carrying EDMX quirks over silently: hand-made flags like FB-311's computed status, and datetime columns that come back with an unspecified kind.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** What changed between EF6 and EF Core that bites during a migration?

[pause 5s]

There's no EDMX, so every mapping is explicit configuration, and type mappings like decimal precision must match the database. In FeeBilling, a scaffolded configuration maps the annual rate as eighteen comma two against a nine comma six column, so a 0.75 percent rate would be stored as 1 percent on the first write. Lazy loading is off unless you opt in, so code that relied on it either breaks or has to become deliberate loading. Since EF Core 3, untranslatable queries throw, except in the final projection. And concurrent use of one context is detected and throws, which exposes the legacy billing runner's shared context.

**Interviewer:** How do you find N+1 queries?

[pause 5s]

Log the SQL with the database command logging category at Information level, and count commands per request in integration tests with a command interceptor, failing when a list endpoint issues too many. In shared environments, Query Store or tracing. In code review, I look for navigation properties touched inside loops, or a select after `ToList`. Then I replace them with projections, `Include`, or split queries.

**Interviewer:** Why not use the InMemory provider for tests?

[pause 5s]

Because it isn't relational. It doesn't translate LINQ to SQL, and it doesn't enforce constraints, column types, precision or transactions. The FeeBilling precision bug would pass on InMemory. SQLite is closer but still differs. I test against the real engine in a container with Testcontainers, which the repo already does for Accounts.Api.

**Interviewer:** Would you migrate EF6 to EF Core in the same step as moving to .NET 10?

[pause 5s]

Not necessarily. EF6 from 6.3 runs on modern .NET, so for existing data access code I can move the runtime first and the ORM second, which separates two risky changes. For new services, like Billing.Api, I'd use EF Core from the start.

**Interviewer:** When would you keep a stored procedure?

[pause 5s]

When it's a measured hot path, or set-based work that LINQ expresses badly. `usp_GetBillableAum` runs once per account per billing run, so it stays, called through EF Core's raw SQL APIs. I'd port the rest to LINQ with tests, and delete the ones nothing calls, after checking execution statistics, because callers can live outside the repository. Only six of FeeBilling's fourteen procedures are called from C# in this repo.

## Recap

Five things to remember from this lesson.

One: EF Core has no EDMX, so mappings are explicit, and they must match the database. FeeBilling's annual rate mapping would turn 0.75 percent into 1 percent on the first write. Fix it, then test the round trip against SQL Server.

Two: EF6 lazy loading hides N+1 queries in almost every legacy controller. Don't turn proxies on; replace each case with a projection, `Include` or a split query.

Three: EF Core 3 and later throw on untranslatable queries, except in the final projection, and translation can differ, so only real-engine integration tests catch it.

Four: EF Core detects concurrent use of a context and throws. That exception is exposing the legacy runner's shared-context bug.

Five: keep only the stored procedures that earn it, use `ExecuteUpdateAsync` for set-based writes, audit EDMX quirks like FB-311, and never test queries against InMemory.

In the next lesson, we'll look at the question the EDMX and EF Core model raise together: when two ORMs share one schema, who owns changes?
