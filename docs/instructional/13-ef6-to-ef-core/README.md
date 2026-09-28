# 13 · EF6 to EF Core

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-06 · **Prerequisites:** 08, 11

**Audio lesson:** [13-ef6-to-ef-core.mp3](13-ef6-to-ef-core.mp3) · [Transcript](script.md)

## Why this video exists

"What changed between EF6 and EF Core?" sounds like trivia, but it's asked because the differences cause real production bugs during a migration: queries that load ten thousand rows, precision silently lost on write, concurrency bugs that EF6 tolerated and EF Core rejects. FeeBilling has an example of each. The legacy code leans on EF6 lazy loading in almost every controller. The half-finished EF Core model maps `FeeTier.AnnualRate` as `decimal(18,2)` against a `decimal(9,6)` column, so a 0.75% rate would be stored as 1% (handover issue #2). And the billing runner shares one context across `Parallel.ForEach`. This video walks through the model, loading, querying, writing and testing differences, using FeeBilling code for each.

## Learning objectives

By the end, the viewer can:

- Explain how an EDMX Database-First model maps to an EF Core model reverse-engineered with `dotnet ef dbcontext scaffold`, and why EF6-on-modern-.NET is a legitimate intermediate step.
- Find and fix type-mapping mistakes (the `AnnualRate` precision bug) and prove the fix against a real SQL Server.
- Identify N+1 queries caused by lazy loading and replace them with projections, `Include` or split queries.
- Describe EF Core's query translation rules (no silent client evaluation except in the final projection) and how to find untranslatable queries before production does.
- Replace EF6 workarounds (raw SQL for "computed" columns, `Database.SqlQuery`, `DataSet`s) with EF Core equivalents (`ExecuteUpdateAsync`, `SqlQuery<T>`, projections).
- Choose a test strategy that catches translation and mapping bugs (Testcontainers, not the InMemory provider).

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| What changed between EF6 and EF Core that bites during a migration? | No EDMX (fluent configuration instead). Lazy loading is off unless you opt in with proxies. Query translation is different, and EF Core 3+ throws on untranslatable queries except in the final projection. Concurrent use of a context is detected and throws. Type mappings must be explicit (decimal precision, `datetime` vs `datetime2`). Bulk operations (`ExecuteUpdate`/`ExecuteDelete`) and raw SQL APIs differ. |
| How do you find N+1 queries? | Log SQL (`Microsoft.EntityFrameworkCore.Database.Command` at Information), count commands per request in integration tests with an interceptor, and watch Query Store or APM traces. In code, look for navigation access inside loops or `Select` after `ToList()`. |
| Why not use the InMemory provider for tests? | It isn't relational: no SQL translation, no constraints, no column types or precision, no transactions. A test with the wrong `decimal(18,2)` mapping passes on InMemory. SQLite is closer but has different types and functions. Use the real engine in a container. |
| Would you migrate EF6 to EF Core in the same step as moving to .NET 10? | Not necessarily. EF6 (6.3 and later) runs on modern .NET, so the code can move runtimes first and ORMs second. That separates two risky changes, which is the same principle as separating migration from behaviour change. |
| When would you keep a stored procedure? | When it's a measured hot path or does set-based work that LINQ expresses badly (`usp_GetBillableAum` runs once per account per billing run). Port the rest to LINQ with tests, and delete the ones nothing calls. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Data/FeeBilling.edmx` | `annotation:LazyLoadingEnabled="true"`; `StoreGeneratedPattern="Computed"` on `Invoice.Status` |
| `legacy/FeeBilling.Data/Model/FeeBilling.Context.cs` | `throw new UnintentionalCodeFirstException();` and the generated `DbSet`s |
| `legacy/FeeBilling.Data/FeeBilling.Data.csproj`, `legacy/README.md` | The EDMX split into `.csdl`/`.ssdl`/`.msl` embedded resources |
| `src/FeeBilling.Infrastructure/Persistence/FeeBillingDbContext.cs` | "Reverse-engineered ... then trimmed"; FeeSchedule/FeeTier configurations commented out |
| `src/FeeBilling.Infrastructure/Persistence/Configurations/FeeTierConfiguration.cs` | `entity.Property(e => e.AnnualRate).HasPrecision(18, 2);` |
| `database/billing/001-schema.sql` | `AnnualRate decimal(9,6) NOT NULL -- 0.007500 = 0.75%` |
| `legacy/FeeBilling.Web/Controllers/Api/AccountsController.cs` | `// lazy: loads EVERY position for the account, then filters in memory` |
| `legacy/FeeBilling.Web/Controllers/Api/BillingController.cs` | `InvoiceCount = r.Invoices.Count, // lazy load per run`; `ApproveRun` raw SQL (FB-311) |
| `legacy/FeeBilling.Web/Controllers/Api/InvoicesController.cs` | `i.Account.AccountNumber, // lazy load per invoice` |
| `legacy/FeeBilling.Core/AumService.cs` | `db.Database.SqlQuery<decimal>("EXEC dbo.usp_GetBillableAum ...")` |
| `src/FeeBilling.Accounts.Api/Accounts/AccountsEndpoints.cs` | The EF Core way: `AsNoTracking()`, `Include`, async, cancellation tokens |
| `src/FeeBilling.Accounts.Api/Common/FeeScheduleCodes.cs` | `Database.SqlQuery<FeeScheduleCodeRow>(...)` for an unmapped type |
| `tests/FeeBilling.Accounts.Api.Tests/Infrastructure/AccountsApiFixture.cs` | Real SQL Server via Testcontainers, schema from `database/` scripts |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | `HasPrecision(18, 2)` vs `decimal(9,6)`. A 0.75% tier becomes 1% the first time anything writes a schedule. "Nothing writes yet, so nobody noticed." |
| 01:30–04:00 | Two models of one database | EDMX: CSDL (conceptual), SSDL (storage), MSL (mapping), T4-generated classes, and `UnintentionalCodeFirstException` guarding against accidental Code First. EF Core: plain entities plus `IEntityTypeConfiguration<T>` classes, reverse-engineered and trimmed. EF6 runs on modern .NET, so "new runtime, same ORM" is a valid first step for code that isn't being rewritten. |
| 04:00–07:30 | Type mappings and the precision bug | Show the config, the schema and the domain comment ("Stored as decimal(9,6)"). Scaffold the table fresh (demo) to show what the database really says. Fix it with `HasPrecision(9, 6)`, *then* wire the configuration into the context. Prove it with a round-trip test against SQL Server. Also check `datetime` vs `datetime2` columns and `char`/`varchar` (`IsUnicode(false)`, `IsFixedLength()`), as `AccountConfiguration` does. |
| 07:30–11:00 | Lazy loading and N+1 | EDMX has lazy loading on, and the legacy controllers depend on it everywhere: per account, per run, per invoice, and a positions endpoint that loads every position for an account and filters in memory. EF Core has no lazy loading unless you add `Microsoft.EntityFrameworkCore.Proxies` and `UseLazyLoadingProxies()`. Don't: turn each case into a projection. `Include` for aggregates you actually need; `AsSplitQuery()` when several collection includes would multiply rows (cartesian explosion). String `Include("FeeSchedule.Tiers")` still works in EF Core; lambda `Include(...).ThenInclude(...)` is refactor-safe. |
| 11:00–13:30 | Query translation | EF6 throws `NotSupportedException` for methods it can't translate. EF Core 1.x/2.x silently evaluated on the client; EF Core 3.0 and later throw, except in the final `Select`, where client code is allowed and can hide per-row work. Translation of the *same* LINQ can differ (string comparison, date functions, `GroupBy`). Only integration tests against the real engine catch these. |
| 13:30–15:30 | Tracking, concurrency, bulk writes | `AsNoTracking()` for reads (Accounts.Api does). EF Core detects two operations on one context at once and throws; the legacy runner's `Parallel.ForEach` over one `_db` becomes one context per chunk (video 15). FB-311: EDMX marked `Invoice.Status` as `Computed`, so EF6 ignored changes and `ApproveRun` fell back to raw SQL. The column is just a default in the database, so the flag was hand-made. EF Core: `ExecuteUpdateAsync` does the set-based approve in one statement. Audit EDMX files for annotations that don't exist in the schema. |
| 15:30–17:30 | Raw SQL and stored procedures | EF6 `Database.SqlQuery<T>` → EF Core `Database.SqlQuery<T>` (scalar and unmapped types since EF Core 8), `FromSql` for entity types, `ExecuteSql` for commands. Interpolated strings become parameters. You can't compose LINQ over `EXEC`, so materialize first. Of the 14 procedures, keep the hot and set-based ones (`usp_GetBillableAum`), port the rest with tests, and delete the dead ones. `DataSet` exports become projections to typed rows (video 12). |
| 17:30–19:00 | Testing and diagnostics | InMemory vs SQLite vs Testcontainers, with the precision bug as the example that only the real engine catches. SQL logging via the `Microsoft.EntityFrameworkCore.Database.Command` category (Accounts.Api sets it to `Warning`), a `DbCommandInterceptor` that counts commands per request, and Query Store in shared environments. |
| 19:00–20:00 | Recap | Map explicitly, load deliberately, never share a context across threads, and test against the real database. |

### Before

```csharp
// src/FeeBilling.Infrastructure/Persistence/Configurations/FeeTierConfiguration.cs
entity.Property(e => e.LowerBound).HasPrecision(19, 2);
entity.Property(e => e.UpperBound).HasPrecision(19, 2);
entity.Property(e => e.AnnualRate).HasPrecision(18, 2);
```

```csharp
// legacy/FeeBilling.Web/Controllers/Api/AccountsController.cs
var positions = account.Positions        // lazy: loads EVERY position for the account, then filters in memory
    .Where(p => p.AsOfDate == asOf.Date)
    .OrderBy(p => p.SecurityCode)
    .Select(p => new { p.SecurityCode, p.SecurityName, p.Quantity, p.MarketValue, p.IsCashSleeve });
```

```csharp
// legacy/FeeBilling.Web/Controllers/Api/BillingController.cs
// Invoice.Status is StoreGeneratedPattern=Computed in the EDMX (FB-311), so EF
// silently ignores changes to it. Update with SQL instead.
var user = HttpContext.Current.User.Identity.Name;
var updated = db.Database.ExecuteSqlCommand(
    "UPDATE dbo.Invoices SET Status = 'Approved', ApprovedBy = @p0, ApprovedOn = GETDATE() WHERE RunId = @p1 AND Status = 'Draft'",
    user, id);
```

### After (sketches)

```csharp
// Mapping matches the column
entity.Property(e => e.AnnualRate).HasPrecision(9, 6);
```

```csharp
// Round-trip test against real SQL Server (Testcontainers). Read back with raw SQL so the
// mapping under test can't mask the stored value.
[Fact]
public async Task FeeTier_AnnualRate_RoundTripsSixDecimalPlaces()
{
    var ct = TestContext.Current.CancellationToken;

    await using (var write = fixture.CreateDbContext())
    {
        var tier = await write.Set<FeeTier>().OrderBy(t => t.Id).FirstAsync(ct);
        tier.AnnualRate = 0.0075m;
        await write.SaveChangesAsync(ct);
    }

    await using var read = fixture.CreateDbContext();
    var stored = await read.Database
        .SqlQuery<decimal>($"SELECT TOP (1) AnnualRate AS Value FROM dbo.FeeTiers ORDER BY Id")
        .SingleAsync(ct);

    Assert.Equal(0.0075m, stored);
}
```

`fixture.CreateDbContext()` is a helper you'd add to a fixture like `AccountsApiFixture`. Restore the seed value afterwards, or give the test its own database, so other tests aren't affected.

```csharp
// N+1 → one query, only the columns needed
var positions = await db.Positions.AsNoTracking()
    .Where(p => p.AccountId == id && p.AsOfDate == asOf)
    .OrderBy(p => p.SecurityCode)
    .Select(p => new PositionDto(p.SecurityCode, p.SecurityName, p.Quantity, p.MarketValue, p.IsCashSleeve))
    .ToListAsync(ct);

// Per-run lazy Count/Sum → aggregated in SQL
var runs = await db.BillingRuns.AsNoTracking()
    .Where(r => r.FirmId == firmId)
    .OrderByDescending(r => r.RequestedOn)
    .Select(r => new BillingRunSummary(
        r.Id, r.FirmId, r.PeriodEnd, r.Status, r.RequestedBy, r.RequestedOn, r.CompletedOn,
        r.Invoices.Count(),
        r.Invoices.Sum(i => (decimal?)i.Amount) ?? 0m))          // empty run: SUM is NULL
    .ToListAsync(ct);
```

```csharp
// FB-311 without raw SQL: one set-based UPDATE.
// Legacy wrote GETDATE() (SQL Server local time). Switching ApprovedOn to UTC is a behaviour
// change: decide it, document it, and don't mix the two in one column silently.
var approved = await db.Invoices
    .Where(i => i.RunId == runId && i.Status == "Draft")
    .ExecuteUpdateAsync(s => s
        .SetProperty(i => i.Status, "Approved")
        .SetProperty(i => i.ApprovedBy, userName)
        .SetProperty(i => i.ApprovedOn, approvedOn), ct);
```

```csharp
// Keep the hot stored procedure; don't compose LINQ over EXEC
private sealed class BillableAumRow
{
    public decimal BillableAum { get; set; }
}

var rows = await db.Database
    .SqlQuery<BillableAumRow>($"EXEC dbo.usp_GetBillableAum @AccountId = {accountId}, @AsOfDate = {asOfDate}")
    .ToListAsync(ct);
var billableAum = rows.Single().BillableAum;
```

The row class follows the pattern already used in `FeeScheduleCodes.cs`. Calling `SingleAsync` directly on the query would make EF wrap the `EXEC` in a subquery, which SQL Server rejects.

## Demo

```bash
docker compose up -d
dotnet run --project tools/FeeBilling.DbInit --reseed

# What does the database actually say? Scaffold into a throwaway project OUTSIDE the repo
dotnet tool install --global dotnet-ef
dotnet new console -o ~/scratch/ScaffoldCheck && cd ~/scratch/ScaffoldCheck
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet ef dbcontext scaffold "Server=localhost,1433;Database=FeeBilling;User Id=sa;Password=FeeBilling!Passw0rd;TrustServerCertificate=True" \
  Microsoft.EntityFrameworkCore.SqlServer --table FeeTiers --table FeeSchedules --output-dir Scaffolded --no-onconfiguring
grep -n "AnnualRate" Scaffolded/*.cs        # decimal(9, 6), not (18, 2)
cd -

# Where does legacy depend on lazy loading?
git grep -n "lazy" -- legacy/FeeBilling.Web/Controllers

# The existing integration tests run against real SQL Server in a container
dotnet test tests/FeeBilling.Accounts.Api.Tests
```

On a Windows machine with the legacy app running (Accounts.Api needs it for remote authentication, video 10), start Accounts.Api with `--Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Information`, call `/api/households?firmId=1`, and walk through the SQL: one query with a join for `Include(h => h.Accounts)`, then the `FeeScheduleCodes` query. Without legacy, show the same thing by pointing a logger at the test output in a scratch copy of `AccountsApiFixture`.

## Traps to call out

- **Trusting a "scaffolded" configuration.** `FeeTierConfiguration` claims to be scaffolded but disagrees with the database. Re-scaffold and diff when in doubt.
- **Precision bugs that hide until the first write.** Reads of existing data look fine. Also check how values with more decimals than the column allows are rounded or truncated on the way in (the schedule editor sends rates computed in JavaScript floating point); write a test rather than assuming.
- **Turning lazy loading back on to make the port compile.** It reproduces every N+1 in the legacy code. Projections are the fix.
- **Unit-testing queries against InMemory.** It passes queries that fail on SQL Server and ignores precision, constraints and transactions.
- **Composing over stored procedures.** `FromSql`/`SqlQuery` over `EXEC` can't be composed; materialize first.
- **Sharing a context across threads.** EF Core will throw *A second operation was started on this context instance before a previous operation completed*. That exception is finding a bug the legacy code always had.
- **Carrying EDMX quirks over silently.** Hand-edited EDMX flags (like FB-311's `Computed`) and `datetime` columns (which come back as `Kind=Unspecified`, video 11) change behaviour when the model is regenerated from the database.

## Key terms

Database-First · EDMX (CSDL/SSDL/MSL) · reverse engineering / scaffolding · fluent configuration · lazy loading · N+1 · projection · split query · cartesian explosion · client evaluation · change tracking · `ExecuteUpdate` · Testcontainers

## After the video

1. Fix handover issue #2 on a branch: `HasPrecision(9, 6)`, wire the FeeSchedule and FeeTier configurations into `FeeBillingDbContext`, and add the round-trip test.
2. List every lazy-loaded navigation in the legacy controllers and write the projection that replaces each one.
3. Classify the 14 stored procedures in `database/billing/002-stored-procedures.sql` as keep, port or delete, with the evidence for each (who calls it, how often).

## References

- `docs/handover.md`: known issues #2 and #4
- `docs/brasswick-modernization-training-plan.md`: WP-06 (tasks and traps), Day 5 of the training plan
- Microsoft Learn (EF Core): *Porting from EF6 to EF Core*, *Reverse engineering (scaffolding)*, *Loading related data*, *Single vs. split queries*, *Client vs. server evaluation*, *SQL queries*, *ExecuteUpdate and ExecuteDelete*, *Testing against your production database system*
- Microsoft Learn (EF6): *Using EF6 with .NET Core / modern .NET* (verify current EF6 support statement before recording)
