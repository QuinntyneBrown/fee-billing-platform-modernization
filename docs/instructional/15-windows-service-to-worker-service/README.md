# 15 · Windows Service to Worker Service

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-04 (hosting and batch processing) · **Prerequisites:** 06, 08, 13

**Audio lesson:** [15-windows-service-to-worker-service.mp3](15-windows-service-to-worker-service.mp3) · [Transcript](script.md)

## Why this video exists

`FeeBilling.BillingRunner` produces every invoice the company sends, and it's the riskiest code in the solution. It's a Windows Service that polls a table every 30 seconds, fakes an `HttpContext` so the static fee calculators work, shares one `DbContext` across a `Parallel.ForEach`, and wraps a whole firm's run in one distributed transaction. The comment says it best: *"crash at account 40,000 = whole run rolls back, starts over"*. Interviewers ask "how would you replace a Windows Service?" to find out whether you'll just port the timer, or redesign the job so it can be claimed safely, processed in parallel, stopped, and resumed. This video covers the hosting and batch half of WP-04. Video 16 replaces MSDTC with an outbox and messaging.

## Learning objectives

By the end, the viewer can:

- Read the legacy runner and list its concurrency and failure bugs, including ones the comments don't mention.
- Host a .NET 10 Worker Service with `BackgroundService`, `PeriodicTimer`, graceful shutdown, and `AddWindowsService()` only where Windows hosting is still required.
- Claim work atomically so two instances (or two overlapping ticks) never process the same run.
- Split a run into persisted chunks, each processed in its own local transaction with its own `DbContext`, using bounded parallelism.
- Make the run resumable: kill the process mid-run, restart it, and end with no duplicate and no missing invoices.
- Explain why EF Core throwing on concurrent `DbContext` use is good news.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How would you replace a Windows Service in modern .NET? | A Worker Service (`Host.CreateApplicationBuilder` + `BackgroundService`). `AddWindowsService()` if it must stay a Windows Service; otherwise a container or container job. DI, options, `ILogger`, graceful shutdown via the stopping token. Then fix the design, not just the host: atomic claiming, chunking, idempotency. |
| Design a resumable batch job. | Persist the plan (chunks) before processing. Each chunk commits its output **and** its "done" marker in one local transaction. On restart, skip completed chunks. Unique constraints make a replayed chunk fail loudly instead of duplicating. Cancellation rolls back only the chunk in flight. |
| How do you use a `DbContext` in parallel? | You don't share one. `DbContext` isn't thread-safe; EF Core detects concurrent use and throws. Use `IDbContextFactory<T>` for one short-lived context per unit of work, with bounded parallelism (`Parallel.ForEachAsync` + `MaxDegreeOfParallelism`). |
| How do you stop two instances processing the same work? | An atomic claim: a single `UPDATE ... OUTPUT` with `UPDLOCK, READPAST`, or a conditional status transition checked by row count or `rowversion`. Never select-then-update. Add a lease or heartbeat so work claimed by a crashed instance can be reclaimed. |
| What happens to in-flight work during a deploy? | The host signals the stopping token, gives work `HostOptions.ShutdownTimeout` to finish, and the orchestrator's grace period must be at least as long. Anything not committed rolls back and is retried, which is only safe because chunks are idempotent. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.BillingRunner/BillingRunnerService.cs` | `OnStart`, `ProcessQueue`, the `TransactionScope`, `Parallel.ForEach`, `run.Status = "Complete"` outside the scope |
| `legacy/FeeBilling.BillingRunner/Program.cs` | `--console` debugging mode |
| `legacy/FeeBilling.BillingRunner/ProjectInstaller.cs` | `installutil`, domain account "(MSDTC needs network access)" |
| `legacy/FeeBilling.BillingRunner/FakeHttpContext.cs` | The hack that lets static calculators find a `DbContext` |
| `legacy/FeeBilling.Data/DbContextFactory.cs` | Why every thread given the same fake context shares one `FeeBillingEntities` |
| `database/billing/001-schema.sql` | `BillingRunQueue` (`StartedOn`, `ErrorMessage` never written by the runner); `Invoices` has no unique key on `(RunId, AccountId)` |
| `database/billing/002-stored-procedures.sql` | `usp_GetPendingBillingRun` ("BillingRunner uses a LINQ query instead") and `usp_CompleteBillingRun`, both unused |
| `src/FeeBilling.Billing.Worker/` (future, WP-04) | Target project |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Read the "crash at account 40,000" comment aloud. Then reveal it's worse: a crash in the wrong millisecond *double-bills* instead of restarting. |
| 01:30–05:30 | Code review of the legacy runner | Walk through the bug list below. Stress that none of it is visible in the MSTest suite. |
| 05:30–08:30 | The Worker Service host | `Host.CreateApplicationBuilder`, `BackgroundService.ExecuteAsync`, `PeriodicTimer` (ticks can't overlap), `AddWindowsService()` (a no-op when not running as a service), `ShutdownTimeout`, and the default that an unhandled exception in a `BackgroundService` stops the host instead of being swallowed. |
| 08:30–11:00 | Claiming work atomically | `UPDATE ... OUTPUT` with `UPDLOCK, READPAST`. Live two-session demo. Why select-then-update is broken, and worse under read committed snapshot isolation (the Azure SQL default). Leases for crashed claimers. |
| 11:00–14:30 | Chunks, contexts and parallelism | A persisted chunk table, one `DbContext` per chunk from `IDbContextFactory`, `Parallel.ForEachAsync` with a bound, a local transaction per chunk, and the execution strategy that retrying connections require. |
| 14:30–17:00 | Resumability | The acceptance test: kill mid-run, restart, assert invoice count equals billable accounts and no `(RunId, AccountId)` repeats. Walk through what happens to the chunk in flight. |
| 17:00–18:30 | Performance | Count round trips: legacy makes at least three per account. The chunked design loads inputs set-based. Target: 50,000 accounts in under 10 minutes locally. Measure it and report the number. |
| 18:30–20:00 | Recap and hand-off | The five properties: hosted, claimable, chunked, idempotent, resumable. Video 16 removes the `TransactionScope` and makes the reporting write eventual. |

### What's wrong with the legacy runner

```csharp
protected override void OnStart(string[] args)
{
    HttpContext.Current = FakeHttpContext.Create();   // so FeeCalculator works outside IIS
    _fakeContext = HttpContext.Current;
    _timer = new Timer(30_000);
    _timer.Elapsed += (s, e) => ProcessQueue();
    _timer.Start();
    Log.Info("BillingRunner started");
}
```

```csharp
var run = _db.BillingRunQueue.FirstOrDefault(r => r.Status == "Pending");
if (run == null) return;
...
            _db.SaveChanges();
            _reportingDb.SaveChanges();
            scope.Complete();
        }
        run.Status = "Complete";   // crash at account 40,000 = whole run rolls back, starts over
        run.CompletedOn = DateTime.Now;
        _db.SaveChanges();
```

| Bug | Consequence |
|---|---|
| `System.Timers.Timer` raises `Elapsed` on the thread pool every 30 s with no reentrancy guard | A run longer than 30 s is picked up **again** by the next tick, because it's still `Pending` |
| The run is never marked `Running`, and `StartedOn` is never set | Nothing stops a second tick or a second instance from claiming it |
| `FirstOrDefault` with no `ORDER BY` | Which pending run goes first is undefined (the unused `usp_GetPendingBillingRun` orders by `RequestedOn`) |
| No `try/catch`; `System.Timers.Timer` suppresses exceptions from `Elapsed` handlers (documented behaviour; verify for your runtime) | A failing run stays `Pending` and is retried every 30 s forever. `ErrorMessage` and `usp_CompleteBillingRun` are never used |
| `_db` and `_reportingDb` live as long as the service | The change tracker grows run after run, and tracked entities can return stale values |
| Every pool thread is given the same `_fakeContext`, so `DbContextFactory.Current` returns one shared context | `Parallel.ForEach` runs the fee calculators against one `FeeBillingEntities` concurrently (FB-402 fixed the symptom, not the cause) |
| Invoices commit inside the scope; the status update happens after it | A crash between `scope.Complete()` and the next `SaveChanges` leaves the invoices committed and the run `Pending`. The next tick bills the firm again, and nothing in the schema prevents duplicate `(RunId, AccountId)` rows |
| `DateTime.Now` | Local server time in `CompletedOn`, `LoadedOn` |

### Target: the host

```csharp
// src/FeeBilling.Billing.Worker/Program.cs (future project, WP-04)
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(o => o.ServiceName = "FeeBilling.BillingRunner");   // no-op unless run as a Windows Service
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContextFactory<BillingDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("FeeBilling"), sql => sql.EnableRetryOnFailure()));
builder.Services.AddSingleton<ChunkProcessor>();
builder.Services.AddHostedService<BillingRunWorker>();

builder.Build().Run();
```

```csharp
public sealed class BillingRunWorker(
    IDbContextFactory<BillingDbContext> dbFactory, ChunkProcessor chunks, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        do
        {
            if (await TryClaimNextRunAsync(stoppingToken) is { } run)
            {
                await chunks.ProcessRunAsync(run, stoppingToken);   // skips chunks already Complete
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));   // no overlap: the next wait starts after the work
    }

    private async Task<ClaimedRun?> TryClaimNextRunAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var claimed = await db.Database.SqlQuery<ClaimedRun>($"""
            WITH next AS (
                SELECT TOP (1) *
                FROM dbo.BillingRunQueue WITH (ROWLOCK, UPDLOCK, READPAST)
                WHERE Status = 'Pending'
                ORDER BY RequestedOn, Id)
            UPDATE next
            SET Status = 'Running', StartedOn = GETDATE()   -- same local-time convention as the rest of this column, for now
            OUTPUT inserted.Id, inserted.FirmId, inserted.PeriodEnd
            """).ToListAsync(ct);   // materialize directly: don't compose LINQ over UPDATE ... OUTPUT
        return claimed.SingleOrDefault();
    }
}
```

When messaging arrives (video 16), the `PeriodicTimer` loop becomes a Service Bus processor, but the claim, chunk and resume logic stays the same.

### Target: chunks

```sql
-- Expand-only schema changes (video 14): legacy ignores both.
CREATE TABLE dbo.BillingRunChunks
(
    RunId        int          NOT NULL,
    ChunkNo      int          NOT NULL,
    Status       varchar(20)  NOT NULL CONSTRAINT DF_BillingRunChunks_Status DEFAULT ('Pending'),
    CompletedOn  datetime2(3) NULL,
    CONSTRAINT PK_BillingRunChunks PRIMARY KEY (RunId, ChunkNo)
);
-- Plus a table (or columns) listing which billing units belong to each chunk, written once when the run starts.

CREATE UNIQUE INDEX UQ_Invoices_RunId_AccountId ON dbo.Invoices (RunId, AccountId);   -- check existing data for duplicates first
```

```csharp
await Parallel.ForEachAsync(pendingChunks,
    new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism, CancellationToken = ct },
    async (chunk, token) =>
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);   // one context per chunk, never shared
        var strategy = db.Database.CreateExecutionStrategy();               // required: EnableRetryOnFailure + explicit transaction
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();                                       // a retry re-runs this lambda
            await using var tx = await db.Database.BeginTransactionAsync(token);

            var inputs = await LoadInputsAsync(db, run, chunk, token);      // set-based: a few queries per chunk, not per account
            db.Invoices.AddRange(engine.Calculate(inputs).Select(r => r.ToInvoice(run.Id)));
            await db.SaveChangesAsync(token);

            await db.BillingRunChunks
                .Where(c => c.RunId == run.Id && c.ChunkNo == chunk.ChunkNo)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, "Complete")
                    .SetProperty(c => c.CompletedOn, clock.GetUtcNow().UtcDateTime), token);

            await tx.CommitAsync(token);                                    // invoices and "chunk done" commit together
        });
    });
```

## Demo

```bash
docker compose up -d
dotnet run --project tools/FeeBilling.DbInit -- --reseed

# The legacy runner's debug mode (Windows only; the real thing also needs MSDTC)
dotnet build legacy/FeeBilling.sln -c Release
legacy/FeeBilling.BillingRunner/bin/Release/net472/FeeBilling.BillingRunner.exe --console

# What the replacement starts from
dotnet new worker -n FeeBilling.Billing.Worker -o src/FeeBilling.Billing.Worker --dry-run
```

**Atomic claim, live.** Enqueue two runs, then open two query windows in any SQL client:

```sql
EXEC FeeBilling.dbo.usp_EnqueueBillingRun @FirmId = 1, @PeriodEnd = '2026-09-30', @RequestedBy = 'demo';
EXEC FeeBilling.dbo.usp_EnqueueBillingRun @FirmId = 2, @PeriodEnd = '2026-09-30', @RequestedBy = 'demo';

-- Window A: claim, but don't commit yet
BEGIN TRAN;
WITH next AS (SELECT TOP (1) * FROM FeeBilling.dbo.BillingRunQueue WITH (ROWLOCK, UPDLOCK, READPAST)
              WHERE Status = 'Pending' ORDER BY RequestedOn, Id)
UPDATE next SET Status = 'Running', StartedOn = GETDATE()
OUTPUT inserted.Id, inserted.FirmId;

-- Window B: the same statement returns the *other* run immediately (READPAST skips A's locked row)
-- Then, in B, run the legacy-style "SELECT TOP (1) ... WHERE Status = 'Pending'" and discuss what it
-- sees under locking read committed vs read committed snapshot.

-- Window A:
ROLLBACK;
```

**The resumability test to write** (WP-04 acceptance): start a run of the 500 generated LAURENT accounts, cancel the host after N chunks commit, start it again, and assert `COUNT(*) = COUNT(DISTINCT AccountId)` for the run and that every billable account has exactly one invoice.

## Traps to call out

- **Porting the timer.** A `PeriodicTimer` fixes overlap within one process. It does nothing about two instances; only the atomic claim does.
- **Select-then-update claiming.** Under read committed snapshot isolation (on by default in Azure SQL Database), both instances read `Pending` and both proceed.
- **Claims with no expiry.** A worker that dies leaves its run `Running` forever. Add a lease (`ClaimedUntil`, heartbeat) or let message redelivery (video 16) drive retries.
- **Chunking by account when billing is by household.** Household allocation needs every member's AUM. The chunk unit is the *billing unit* (an account, or a whole household), or members land in different chunks.
- **Re-slicing on restart.** Chunk membership is decided once and persisted. Recomputing it after a restart (accounts opened or closed in the meantime) moves accounts between completed and pending chunks.
- **`EnableRetryOnFailure` plus `BeginTransaction`.** EF Core throws unless the work runs inside `CreateExecutionStrategy().ExecuteAsync`, and the lambda must be safe to re-run.
- **"Commit outcome unknown".** A connection drop during commit can hide a successful commit, so the retry inserts again. The unique index turns that into a clear error. Treat a violation on an already-`Complete` chunk as success.
- **Shutdown timeouts that don't match.** The host's `ShutdownTimeout` default is 30 seconds (verify), and a container orchestrator's grace period may be shorter. A chunk still running at kill time rolls back, which is fine, but only because it's idempotent.
- **Blaming EF Core.** `InvalidOperationException: A second operation was started on this context instance...` is EF Core reporting a bug the legacy code always had.

## Key terms

Worker Service · `BackgroundService` · `PeriodicTimer` · graceful shutdown · atomic claim · `READPAST` / `UPDLOCK` · lease · chunk · billing unit · `IDbContextFactory<T>` · bounded parallelism · execution strategy · idempotency · resumability

## After the video

1. Write the chunk-table migration as an expand-only change, and a query that proves no duplicate `(RunId, AccountId)` rows exist in the seed data before the unique index is added.
2. Build the claim query into a small repository class and test it with two concurrent claimers against Testcontainers.
3. Write the kill-and-restart acceptance test, then measure a 50,000-account run and record accounts per second.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 6.3 (`BillingRunnerService.cs`), Section 9 WP-04, Section 12.3
- `docs/handover.md`: "Replace the BillingRunner Windows Service"
- Microsoft Learn: *Worker services in .NET*, *Create a Windows Service using BackgroundService*, *DbContext lifetime, configuration, and initialization*, *Connection resiliency (execution strategies)*
- SQL Server docs: table hints `READPAST` and `UPDLOCK`; `OUTPUT` clause
