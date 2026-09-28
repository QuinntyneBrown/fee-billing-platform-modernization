# 14 · Shared Database and Schema Ownership

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-06 · **Prerequisites:** 01, 13

## Why this video exists

Every strangler-fig migration that shares a database eventually hits the same question: two applications, two ORMs, one schema, so **who is allowed to change it, and how?** FeeBilling has left that question open since ADR-0004 was accepted. The schema is described by *three* models (the EF6 EDMX, the partial EF Core model, and a Code First `ReportingEntities` for the second database), and nobody owns any of them. Interviewers use this topic to separate people who have run a long migration in production from people who have only done greenfield work. This video settles ownership, shows how to baseline EF Core migrations against a database that already exists, and teaches expand/contract so that every schema change stays safe for both apps.

## Learning objectives

By the end, the viewer can:

- Explain why sharing the database was the right call for the first slices, and what it costs over time.
- Choose a schema owner and justify it. The recommendation here: EF Core migrations become the source of truth, and the EDMX becomes read-only.
- Baseline EF Core migrations against an existing schema with an empty initial migration, and explain what `__EFMigrationsHistory` records.
- Apply expand/contract (parallel change) to any schema change while legacy still reads and writes the same tables.
- Deploy migrations as a separate, reviewed step (idempotent script or migration bundle) instead of from app startup.
- Find out which of the 14 stored procedures are still called before deciding who owns them.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| Two ORMs share one schema. Who owns changes? | Exactly one owner, written down in an ADR. EF Core migrations (reviewed in PRs, versioned with the code that needs them) are the source of truth. The EDMX is regenerated from the database, never edited by hand. Every change must stay backward compatible with legacy until legacy is retired. |
| How do you change a schema that an old app and a new app both use? | Expand/contract. Add a nullable column or a new table, backfill, dual-write or sync, switch reads, and remove the old shape only once no consumer needs it. Never rename or drop in one step. Additive, nullable changes are safe for EF6; NOT NULL without a default breaks legacy inserts. |
| Should the app run migrations on startup? | Not in a multi-instance or regulated deployment. The app would need DDL permissions, several instances race each other, a failed migration takes the app down mid-rollout, and nobody reviewed the SQL. Generate an idempotent script or a bundle, review it, and run it as a pipeline step. |
| How do you know whether a stored procedure is still used? | The repository only shows what the repository calls. Check `sys.dm_exec_procedure_stats` and Query Store, or run an Extended Events session for a **full billing cycle**, because quarter-end jobs only run once a quarter. |
| When do you split the shared database? | Once ownership is clear table by table and no cross-service joins remain. Get there with schemas per service, views as contracts, then CDC or events to feed copies. Deferred is not abandoned. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `docs/adr/0004-shared-database-during-transition.md` | The decision, the "two models can drift" consequence, and the **Open questions** section |
| `docs/handover.md` | Known issue #4: "No one decided who owns schema changes" |
| `database/billing/001-schema.sql` | Header comment ("described by TWO models ... Nobody has decided"); `FeeTiers.AnnualRate decimal(9,6)`; `Accounts.LastValuedAt datetime2(0)` |
| `legacy/FeeBilling.Data/FeeBilling.edmx`, `Model/FeeBilling.Context.cs` | Database-First model; `throw new UnintentionalCodeFirstException()` |
| `legacy/FeeBilling.Data/Reporting/ReportingEntities.cs` | The third model: Code First against an existing database, `SetInitializer<ReportingEntities>(null)` |
| `src/FeeBilling.Infrastructure/Persistence/FeeBillingDbContext.cs` | "Reverse-engineered ... then trimmed"; FeeSchedule and FeeTier configurations not applied |
| `src/FeeBilling.Infrastructure/Persistence/Configurations/FeeTierConfiguration.cs` | `HasPrecision(18, 2)` against a `decimal(9,6)` column: drift the model can't see |
| `database/billing/002-stored-procedures.sql` | "Nobody has checked which is which" |
| `tools/FeeBilling.DbInit/SqlScriptRunner.cs`, `tests/FeeBilling.Accounts.Api.Tests/Infrastructure/AccountsApiFixture.cs` | Tests build the schema from the real SQL scripts, not from the EF model |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Three models, two databases, zero owners. Show the header of `001-schema.sql` next to `FeeBilling.edmx`, `FeeBillingDbContext.cs` and `ReportingEntities.cs`. Ask: if a developer adds a column tomorrow, which file do they edit? |
| 01:30–04:00 | ADR-0004 in context | Why sharing was right: the first slices shipped with no data synchronization. What it costs: drift between models, and any schema change can break legacy, the new services, or both. The open question is the subject of this video. |
| 04:00–07:00 | Options and a recommendation | (1) DBA-owned SQL scripts (DbUp, SSDT). (2) EF Core migrations. (3) The EDMX. Database-First never owned the schema, so it's not a real option. Recommend EF Core migrations: the change lives in the same PR as the code that needs it, it's reviewed, and it's one tool for all new work. The EDMX is regenerated from the database, read-only. Write it as an ADR that answers ADR-0004's open question. |
| 07:00–10:30 | Baselining an existing schema | Live demo: add the design package, generate `Baseline`, empty its body, apply it. `__EFMigrationsHistory` gets one row and nothing else changes. Explain why the snapshot, not the database, is what EF diffs, and the trap that follows: an existing table mapped *after* the baseline produces a `CreateTable`. |
| 10:30–14:00 | Expand/contract | Two worked examples below: an additive column for idempotency keys, and moving `LastValuedAt` to `datetimeoffset`. Show which steps legacy notices and which it doesn't. |
| 14:00–16:30 | Deploying migrations | `migrations script --idempotent` vs `migrations bundle` vs `Database.Migrate()` at startup. Least privilege: the app's SQL login shouldn't have DDL rights. Where `DbInit` fits: dev and test only. |
| 16:30–18:30 | Stored procedures and the reporting database | Only 6 of the 14 procedures are referenced from C# in this repository. Show the `git grep`, then the DMV query. `FeeBillingReporting` is a second ownership problem, currently written inside the same `TransactionScope` (video 16). |
| 18:30–20:00 | Later: database per service, and recap | Schemas per service and per-service SQL logins as the intermediate step; views as contracts; CDC or events to split. Recap in one sentence: one owner, additive changes only, reviewed scripts, contract after legacy is gone. |

### Baseline migration

The EF Core model maps only Firms, Accounts and Households today. An **empty** baseline records "the schema as found" without touching it:

```csharp
// src/FeeBilling.Infrastructure/Persistence/Migrations/20260927000000_Baseline.cs (future file)
public partial class Baseline : Migration
{
    // The schema already exists (database/billing/*.sql). This migration only records that fact.
    protected override void Up(MigrationBuilder migrationBuilder) { }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
```

- **Existing environments:** applying it creates `__EFMigrationsHistory` (if missing) and inserts one row. No DDL runs.
- **Fresh dev and test databases:** `tools/FeeBilling.DbInit` builds the as-found schema from `database/*.sql`, then migrations run on top. `AccountsApiFixture` already builds its container from the same scripts, so tests exercise the real schema and not the model's idea of it.
- **Alternative:** a baseline that contains the full DDL, so migrations alone can build a fresh database. Existing environments then need the history row inserted by hand. It's more work while the SQL scripts still exist, so keep the empty baseline until legacy is retired.

**Trap:** EF diffs the model against the *snapshot*, not the live database. When `FeeScheduleConfiguration` and `FeeTierConfiguration` are wired in later, the next migration will contain `CreateTable("FeeTiers")` for a table that already exists. Either map every table you intend to own *before* generating the baseline, or edit that later migration down to nothing and say why in the PR.

### Model drift the migrations can't see

`FeeTierConfiguration.cs` says `HasPrecision(18, 2)`; the column is `decimal(9,6)`. The model and the database disagree, and neither `has-pending-model-changes` nor a baseline will notice, because both compare the model with itself. Add a drift test that compares EF metadata with `INFORMATION_SCHEMA.COLUMNS`:

```csharp
[Fact]
public async Task EfModel_MatchesDatabaseColumnTypes()
{
    await using var db = CreateContext();   // points at the Testcontainers database built from database/*.sql
    var actual = await db.Database
        .SqlQuery<ColumnInfo>($"""
            SELECT TABLE_NAME AS [Table], COLUMN_NAME AS [Column], DATA_TYPE AS DataType,
                   NUMERIC_PRECISION AS [Precision], NUMERIC_SCALE AS Scale
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
            """)
        .ToListAsync();

    foreach (var entity in db.Model.GetEntityTypes())
    foreach (var property in entity.GetProperties())
    {
        var column = actual.Single(c => c.Table == entity.GetTableName() && c.Column == property.GetColumnName());
        Assert.Equal(Normalize(column), property.GetColumnType());   // "decimal(9,6)" vs "decimal(18,2)" fails here
    }
}

// ColumnInfo is a record matching the aliases above; Normalize turns ("decimal", 9, 6) into "decimal(9,6)".
```

### Expand/contract, twice

**Example 1: additive, safe for legacy.** `Billing.Api` needs an idempotency key on runs (video 12).

```sql
-- Expand. Nullable, so usp_EnqueueBillingRun and the EDMX keep working unchanged.
ALTER TABLE dbo.BillingRunQueue ADD IdempotencyKey varchar(64) NULL;
CREATE UNIQUE INDEX UQ_BillingRunQueue_IdempotencyKey
    ON dbo.BillingRunQueue (IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
-- No contract step: the column stays.
```

EF6 Database-First only selects the columns in its SSDL, so extra nullable columns are invisible to legacy. One caveat to check before shipping: modifying a table that has a filtered index needs `QUOTED_IDENTIFIER ON`, and a stored procedure keeps the setting it was created with. Check `sys.sql_modules.uses_quoted_identifier` for `usp_EnqueueBillingRun` before adding the index (verify on the target server).

**Example 2: a type change, which needs all five steps.** `Accounts.LastValuedAt` is `datetime2(0)` holding UTC by convention (the source of handover issue #3). Changing its type in place breaks the EDMX, which expects `DateTime`.

| Step | Change | Legacy notices? |
|---|---|---|
| Expand | Add `LastValuedAtUtc datetimeoffset(0) NULL` | No |
| Backfill | `UPDATE ... SET LastValuedAtUtc = TODATETIMEOFFSET(LastValuedAt, 0)` in batches | No |
| Dual write | The nightly valuation job writes both columns (or a trigger keeps them in sync) | No |
| Switch reads | EF Core maps `LastValuedAtUtc`; the new APIs read it | No |
| Contract | Drop `LastValuedAt` **after** legacy and the EDMX are retired (WP-10 checklist) | Legacy is gone |

## Demo

```bash
docker compose up -d
dotnet run --project tools/FeeBilling.DbInit -- --reseed

# Which procedures does this repository actually call? (6 of 14)
git grep -ohE "usp_[A-Za-z]+" -- legacy src | sort -u
grep -oE "CREATE PROCEDURE dbo\.usp_[A-Za-z]+" database/billing/002-stored-procedures.sql

# Baseline. The startup project needs the design package (its version is already pinned in Directory.Packages.props):
#   <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
dotnet ef migrations add Baseline \
  --project src/FeeBilling.Infrastructure --startup-project src/FeeBilling.Accounts.Api \
  --output-dir Persistence/Migrations
# Empty the Up/Down bodies, then:
dotnet ef migrations script --idempotent \
  --project src/FeeBilling.Infrastructure --startup-project src/FeeBilling.Accounts.Api -o baseline.sql
dotnet ef database update \
  --project src/FeeBilling.Infrastructure --startup-project src/FeeBilling.Accounts.Api
dotnet ef migrations has-pending-model-changes \
  --project src/FeeBilling.Infrastructure --startup-project src/FeeBilling.Accounts.Api

# The deployable alternative to a script: a self-contained executable
dotnet ef migrations bundle \
  --project src/FeeBilling.Infrastructure --startup-project src/FeeBilling.Accounts.Api -o efbundle
```

Then, in SQL, show what ran and which procedures have been used since the last restart:

```sql
SELECT * FROM FeeBilling.dbo.__EFMigrationsHistory;

SELECT OBJECT_NAME(ps.object_id, ps.database_id) AS proc_name, ps.execution_count, ps.last_execution_time
FROM sys.dm_exec_procedure_stats AS ps
WHERE ps.database_id = DB_ID('FeeBilling')
ORDER BY ps.last_execution_time DESC;
```

If design-time context creation fails (for example because `Program.cs` throws when `RemoteApp:Url` is missing), add an `IDesignTimeDbContextFactory<FeeBillingDbContext>` to the Infrastructure project rather than weakening startup validation.

## Traps to call out

- **"The ORM will figure it out."** Migrations diff the model against the snapshot. They never look at the live database, so existing drift (the `decimal(18,2)` rate) stays invisible.
- **Mapping an existing table after the baseline.** The next migration tries to `CREATE` it.
- **Renaming or dropping in one step.** EF generates a perfectly valid `RenameColumn`, and legacy breaks at its next query. Every destructive change waits for the contract phase.
- **NOT NULL without a default.** Legacy inserts don't know about the column and fail.
- **A schema change that's really a behaviour change.** A unique index on `BillingRunQueue (FirmId, PeriodEnd)` makes legacy's double-click throw instead of creating a second run. That might be right, but it changes legacy behaviour through the database, so it needs the same sign-off as a code change. It also fails to create if duplicates already exist.
- **`Database.Migrate()` at startup.** Newer EF Core versions lock against concurrent migrators (verify for your version), but the permission and reviewability problems remain.
- **Deleting "unused" procedures based on a repo search.** SSRS reports, SQL Agent jobs, the month-end export tool and the `FeedProcessor` scheduled task aren't in this repository. Observe for a full quarter.
- **Forgetting the reporting database.** `ReportingEntities` is a third model with its own owner question, and the previous team was reassigned to the Reporting platform.

## Key terms

Schema ownership · source of truth · baseline migration · model snapshot · `__EFMigrationsHistory` · schema drift · expand/contract (parallel change) · backfill · idempotent migration script · migration bundle · least privilege · database per service · CDC

## After the video

1. Write the ADR that answers ADR-0004's open question: owner, tool, review rule, deployment step, and the expand/contract rule.
2. Add the baseline migration and the drift test. Confirm the drift test fails on `FeeTiers.AnnualRate` until video 13's fix is applied.
3. Draft a table-by-table ownership map (Accounts.Api, Billing.Api, Ingestion, Reporting) as the first step towards schemas per service.

## References

- `docs/adr/0004-shared-database-during-transition.md`, `docs/handover.md` (known issue #4)
- `docs/brasswick-modernization-training-plan.md`: Section 9, WP-06
- Microsoft Learn: *EF Core migrations: applying migrations*, *Migration bundles*, *Using migrations with an existing database*
- Martin Fowler, *ParallelChange* (bliki); Pramod Sadalage and Scott Ambler, *Refactoring Databases*
