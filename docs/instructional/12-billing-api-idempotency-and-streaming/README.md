# 12 · Billing API: Idempotency, CQRS and Streaming

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-03 · **Prerequisites:** 07, 09, 11

**Audio lesson:** [12-billing-api-idempotency-and-streaming.mp3](12-billing-api-idempotency-and-streaming.mp3) · [Transcript](script.md)

## Why this video exists

Two legacy behaviours in the billing endpoints are really design problems. First, `POST /api/billing/runs` has no duplicate protection anywhere: a double-click queues two runs, and two runs mean double-billed clients (FB-247). Second, the invoice endpoints load an entire run into memory, as a `DataSet` in one place and with a lazy-load per invoice in another, for firms with 40,000 accounts. This video designs the replacement Billing API: an idempotent `StartBillingRun` that holds up under concurrent requests, vertical slices without a mediator library, errors as ProblemDetails, and invoice listing and CSV export that use constant memory.

## Learning objectives

By the end, the viewer can:

- Explain why disabling the button isn't idempotency, and design an `Idempotency-Key` flow (store, replay, mismatch) backed by database constraints.
- Handle the concurrent-duplicate race with a unique constraint rather than a check-then-insert.
- Combine an idempotency key (for retries by API clients) with a natural-key constraint (for the unmodified AngularJS UI).
- Structure endpoints as vertical slices with a hand-rolled command/query handler, and justify not adding MediatR.
- Replace offset or no paging with keyset (cursor) paging, and stream a CSV export with `IAsyncEnumerable` without buffering the run.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do you make a POST idempotent? | The client sends an `Idempotency-Key`. The server stores the key, a hash of the request and the result in the same transaction as the side effect, protected by a unique constraint. A repeat with the same key and body replays the stored result; the same key with a different body is rejected (422). Concurrent duplicates are resolved by the constraint: the loser catches the duplicate-key error and returns the winner's result. |
| Isn't a unique constraint on (firm, period) enough? | It stops the double-click, and it's what protects the legacy UI (which sends no key). It doesn't let an API client safely retry after a timeout and get the *same* response, and it can't distinguish a deliberate rerun. You want both: a natural key for business uniqueness and an idempotency key for retry safety. |
| Offset or cursor paging? | Offset (`Skip/Take`) gets slower as the offset grows and skips or duplicates rows when data changes between pages. Keyset paging (`WHERE Id > @after ORDER BY Id`) is an index seek every time and stable under inserts. Use offset only for small, static lists. |
| How do you export 50,000 rows without loading them into memory? | Project to a narrow row type, enumerate with `AsAsyncEnumerable()`, and write each row to the response stream as it arrives. Use invariant-culture formatting and async I/O throughout. Memory stays flat no matter how big the run is. |
| Do you need MediatR for CQRS? | No. A command/query type plus a handler interface is about 50 lines, and minimal API endpoints can inject the handler directly. MediatR and AutoMapper moved to commercial licences in 2025, and in a regulated vendor every new dependency needs a licence review (verify current terms). |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Web/Controllers/Api/BillingController.cs` | `StartRun`: `return Ok(new { runId });   // double-click = two billing runs = double-billed clients` |
| `database/billing/002-stored-procedures.sql` | `usp_EnqueueBillingRun`: "Enqueue a run. No duplicate check." |
| `database/billing/001-schema.sql` | `BillingRunQueue` has no unique constraint; `IX_Invoices_RunId` |
| `legacy/FeeBilling.Web/App/billing/billing-review.controller.js` | FB-247 comment: "Server-side de-dupe is on the backlog." |
| `legacy/FeeBilling.Data/InvoiceRepository.cs` | `adapter.Fill(ds);   // whole run in memory; 40k rows for the big firms` |
| `legacy/FeeBilling.Web/Controllers/Api/InvoicesController.cs` | `.ToList()   // no paging` and `i.Account.AccountNumber   // lazy load per invoice` |
| `legacy/FeeBilling.Web/Helpers/CsvExporter.cs` | `value.ToString();   // uses the server's current culture` |
| `legacy/FeeBilling.Web/Web.config` | `<globalization culture="auto" uiCulture="auto" />`: culture follows the browser |
| `docker/servicebus/Config.json` | `billing-runs` queue: `"RequiresDuplicateDetection": false` (the broker won't de-duplicate either) |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Run the enqueue procedure twice with the same arguments (demo). Two `Pending` runs for the same firm and period. In production that's two invoices per account. |
| 01:30–04:00 | Why it happens | No constraint in the table, no check in the procedure, no key in the API. Disabling the button in the UI doesn't help with network retries, proxies retrying, or two users on two machines. Idempotency must be enforced where the data is. |
| 04:00–08:30 | Designing `Idempotency-Key` | The table, the request hash, the three outcomes (new, replay, mismatch → 422). The race: two requests with the same key arrive together; the second one's insert blocks on the first one's uncommitted key and then fails with a duplicate-key error (SQL Server 2627 or 2601), so it replays the winner's result. Then the natural key: `(FirmId, PeriodEnd, RunType)` unique. The unmodified AngularJS app sends no header, so the natural key is what fixes FB-247 for legacy screens. Adding that unique index will fail if production already contains duplicate runs: clean the data first. |
| 08:30–10:30 | Proving it | An integration test that fires ten concurrent POSTs with one key and asserts exactly one run and one distinct `runId` (WebApplicationFactory + Testcontainers, the same pattern as `AccountsApiFixture`). A second test: same key, different body → 422. |
| 10:30–13:00 | Slices, handlers, errors | Folder per feature (`StartBillingRun/`, `GetBillingRun/`, `ListInvoices/`, `PreviewFee/`), each with request, handler, endpoint. A handler interface instead of MediatR. Validation: FluentValidation or hand-rolled; .NET 10 also adds built-in minimal API validation (verify before recording). Errors as ProblemDetails in `/api/v1`, but the legacy route keeps `{ "Message": ... }` because `billing-review.controller.js` reads `response.data.Message` (video 11). |
| 13:00–14:30 | Versioning and legacy routes | The new API lives at `/api/v1/billing`. The legacy path `/api/billing` is kept alive by a YARP `PathPattern` transform (video 09) until the AngularJS screens are gone. Anything that changes shape goes to v1 only. |
| 14:30–18:30 | Invoices without buffering | Legacy: a 40k-row `DataSet` built before the first byte, plus N+1 lazy loads for `AccountNumber`. New: keyset paging over `(RunId, Id)`, which the existing `IX_Invoices_RunId` index supports because SQL Server adds the clustered key to non-unique non-clustered index keys. CSV: `AsAsyncEnumerable()` to the response stream with invariant formatting. The legacy CSV depends on the *downloader's* browser culture (`culture="auto"`): a `fr-CA` user gets `5312,50`. The sync-I/O trap: disposing a `StreamWriter` synchronously on the response stream throws in ASP.NET Core. |
| 18:30–20:00 | PreviewFee and recap | `POST /api/v1/fees/preview`: runs the pure `FeeEngine` with no persistence, so it's naturally idempotent and safe to retry. Recap: idempotency lives in the database, and exports should stream instead of buffering. |

### Before

```csharp
// legacy/FeeBilling.Web/Controllers/Api/BillingController.cs
[HttpPost, Route("runs")]
public IHttpActionResult StartRun(BillingRunRequest req)
{
    var user = HttpContext.Current.User.Identity.Name;
    var runId = BillingRunService.Enqueue(req.FirmId, req.PeriodEnd, user);
    return Ok(new { runId });   // double-click = two billing runs = double-billed clients
}
```

```sql
-- database/billing/002-stored-procedures.sql
-- 5. Enqueue a run. No duplicate check.
INSERT INTO dbo.BillingRunQueue (FirmId, PeriodEnd, Status, RequestedBy, RequestedOn)
VALUES (@FirmId, @PeriodEnd, 'Pending', @RequestedBy, GETDATE());
```

### After: constraints (sketch, applied as expand-only migrations)

```sql
CREATE TABLE dbo.IdempotencyKeys
(
    Scope           varchar(50)     NOT NULL,   -- e.g. 'StartBillingRun'
    [Key]           varchar(100)    NOT NULL,
    RequestHash     binary(32)      NOT NULL,   -- SHA-256 of the canonical request
    RunId           int             NOT NULL,
    CreatedOn       datetime2(0)    NOT NULL,
    CONSTRAINT PK_IdempotencyKeys PRIMARY KEY (Scope, [Key])
);

-- Natural key. RunType is a new column ('Regular' | 'Rerun' | 'Shadow'); de-duplicate existing rows first.
CREATE UNIQUE INDEX UX_BillingRunQueue_Firm_Period_Type
    ON dbo.BillingRunQueue (FirmId, PeriodEnd, RunType)
    WHERE Status <> 'Cancelled';
```

### After: the handler's race handling (sketch)

```csharp
public async Task<StartRunResult> Handle(StartBillingRun command, CancellationToken ct)
{
    var existing = await db.IdempotencyKeys.AsNoTracking()
        .SingleOrDefaultAsync(k => k.Scope == Scope && k.Key == command.IdempotencyKey, ct);
    if (existing is not null)
    {
        return existing.RequestHash.AsSpan().SequenceEqual(command.RequestHash)
            ? StartRunResult.Replayed(existing.RunId)
            : StartRunResult.KeyReusedWithDifferentRequest();          // 422
    }

    var run = BillingRun.Request(command.FirmId, command.PeriodEnd, command.RequestedBy, time.GetUtcNow());
    db.BillingRuns.Add(run);
    db.IdempotencyKeys.Add(IdempotencyKey.For(Scope, command.IdempotencyKey, command.RequestHash, run));

    try
    {
        await db.SaveChangesAsync(ct);                                 // one transaction: run + key (+ outbox row, video 16)
        return StartRunResult.Created(run.Id);
    }
    catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
    {
        // Lost the race to a concurrent request with the same key (or the same firm/period).
        db.ChangeTracker.Clear();
        var winner = await db.IdempotencyKeys.AsNoTracking()
            .SingleOrDefaultAsync(k => k.Scope == Scope && k.Key == command.IdempotencyKey, ct);
        return winner is not null
            ? StartRunResult.Replayed(winner.RunId)
            : StartRunResult.RunAlreadyExists();                        // natural-key conflict: 409
    }
}
```

### After: keyset paging and a streamed CSV (sketch)

```csharp
group.MapGet("/runs/{runId:int}/invoices", async (int runId, int? after, int? limit, FeeBillingDbContext db, CancellationToken ct) =>
{
    var take = Math.Clamp(limit ?? 500, 1, 1000);
    var rows = await db.Invoices.AsNoTracking()
        .Where(i => i.RunId == runId && i.Id > (after ?? 0))
        .OrderBy(i => i.Id)
        .Take(take + 1)
        .Select(i => new InvoiceRow(i.Id, i.AccountId, i.Account.AccountNumber, i.Account.AccountName, i.HouseholdId, i.Amount, i.Status))
        .ToListAsync(ct);

    var next = rows.Count > take ? rows[take - 1].Id : (int?)null;
    return TypedResults.Ok(new InvoicePage(rows.Take(take).ToList(), next));
});

group.MapGet("/runs/{runId:int}/invoices.csv", (int runId, FeeBillingDbContext db, CancellationToken ct) =>
    Results.Stream(async body =>
    {
        await using var writer = new StreamWriter(body, new UTF8Encoding(false), bufferSize: 16 * 1024, leaveOpen: true);
        await writer.WriteLineAsync("InvoiceId,AccountNumber,Amount");

        var rows = db.Invoices.AsNoTracking()
            .Where(i => i.RunId == runId)
            .OrderBy(i => i.Id)
            .Select(i => new { i.Id, i.Account.AccountNumber, i.Amount })
            .AsAsyncEnumerable();

        await foreach (var row in rows.WithCancellation(ct))
        {
            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{row.Id},{row.AccountNumber},{row.Amount}"));
        }
    },
    contentType: "text/csv",
    fileDownloadName: $"invoices-{runId}.csv"));
```

`await using` makes the final flush asynchronous. A plain `using` would flush synchronously on dispose, and ASP.NET Core disallows synchronous I/O on the response body by default. Account numbers are alphanumeric (see `AccountNumber.Create`), so they need no CSV escaping here; free-text columns such as `AccountName` do.

### Proving idempotency under concurrency (sketch)

```csharp
[Fact]
public async Task StartRun_SameKeyConcurrently_CreatesExactlyOneRun()
{
    var key = Guid.NewGuid().ToString();
    var ct = TestContext.Current.CancellationToken;

    var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/runs")
        {
            Content = JsonContent.Create(new { FirmId = 1, PeriodEnd = "2026-09-30" }),
        };
        request.Headers.Add("Idempotency-Key", key);
        return _client.SendAsync(request, ct);
    }));

    Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
    var runIds = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<RunCreated>(ct)));
    Assert.Single(runIds.Select(r => r!.RunId).Distinct());
}
```

## Demo

```bash
docker compose up -d
dotnet run --project tools/FeeBilling.DbInit --reseed

# The double-click, reproduced at the database (the mssql/server image ships sqlcmd; azure-sql-edge does not)
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'FeeBilling!Passw0rd' -C -d FeeBilling -Q "
EXEC dbo.usp_EnqueueBillingRun 1, '2026-09-30', 'demo';
EXEC dbo.usp_EnqueueBillingRun 1, '2026-09-30', 'demo';
SELECT Id, FirmId, PeriodEnd, Status, RequestedBy FROM dbo.BillingRunQueue WHERE FirmId = 1;"
```

1. Show the two rows. Then try to add the natural-key unique index and show that it fails on the duplicates you just created: the data-cleanup step is part of the migration.
2. Walk through the concurrent test above and explain what each of the ten requests experiences at the database.
3. Show `InvoiceRepository.GetInvoiceDataSet` and `CsvExporter.FormatValue` side by side with the streamed endpoint, and name every memory and culture difference.

## Traps to call out

- **Check-then-insert.** "If the key exists, return it; otherwise insert" has a race window. Only a unique constraint closes it.
- **Storing the key outside the transaction.** If the key is saved but the run isn't (or the reverse), retries either lose the run or create a second one. Key, run and outbox row commit together.
- **Filtered unique indexes and old stored procedures.** Inserts into a table with a filtered index need `QUOTED_IDENTIFIER ON`, and a stored procedure keeps the setting it was created with. If `usp_EnqueueBillingRun` was deployed with it off in production, legacy enqueues start failing (error 1934) the day the index ships. Verify how the procedures were deployed before choosing a filtered index.
- **Requiring the header on legacy routes.** The unmodified AngularJS app doesn't send it. Make the natural key the protection for legacy callers.
- **Relying on the message broker to de-duplicate.** The `billing-runs` queue has duplicate detection turned off, and even when it's on it only covers a time window. Consumers must be idempotent too (video 16).
- **Offset paging on a 40k-row run.** Page 80 costs 80 pages of reads, and rows shift while a reviewer is paging.
- **Buffering "just this once".** `ToListAsync()` before writing CSV brings back the legacy memory profile. The gateway's activity timeout will also cut long buffered exports (video 09).
- **Culture-dependent CSV.** Legacy output depends on who downloaded it. Use `CultureInfo.InvariantCulture` and document the format.
- **Adding a library to get CQRS.** The pattern is the separation of commands and queries, not the package. Check licences before adding dependencies.

## Key terms

Idempotency key · natural key · unique constraint · replay · check-then-act race · vertical slice · command/query handler · ProblemDetails · keyset (cursor) paging · streaming response · `IAsyncEnumerable<T>`

## After the video

1. Write the WP-03 ADR for idempotency: key scope, retention period for stored keys, the natural key, and what a legitimate rerun looks like (`RunType`).
2. Write the SQL to find existing duplicate runs in `BillingRunQueue`, and a plan for resolving them before the unique index is added.
3. Design the `/api/v1` invoice page contract (fields, `next` cursor, limits) and the legacy-route adapter that still returns `{ "Table": [...] }`.

## References

- `docs/brasswick-modernization-training-plan.md`: WP-03 (tasks, acceptance criteria, traps), Section 2 (licensing), Section 12.3 (system design prompt)
- IETF HTTPAPI working group draft: *The Idempotency-Key HTTP Header Field* (a draft; check its status)
- RFC 9457, *Problem Details for HTTP APIs*
- Microsoft Learn: *Minimal APIs quick reference* (`Results.Stream`, `TypedResults`), *Pagination* (EF Core docs on keyset pagination)
- SQL Server error numbers 2627 (unique constraint violation) and 2601 (duplicate key in unique index)
