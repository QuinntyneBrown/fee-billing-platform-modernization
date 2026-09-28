# 16 · Replacing MSDTC: Outbox and Messaging

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-04 (consistency and messaging) · **Prerequisites:** 15

**Audio lesson:** [16-replacing-msdtc-with-outbox-and-messaging.mp3](16-replacing-msdtc-with-outbox-and-messaging.mp3) · [Transcript](script.md)

## Why this video exists

The legacy billing run writes invoices to `FeeBilling` and fee facts to `FeeBillingReporting` inside one `TransactionScope`. Two connections in one scope promote to a distributed transaction, so production needs MSDTC, a domain service account with network access, and both databases up at the same time. None of that comes with you to Linux containers. "What replaces a distributed transaction?" and "explain the outbox pattern" are standard senior interview questions, and the follow-up is always "and what about duplicates?". This video replaces the `TransactionScope` with local transactions, a transactional outbox, Azure Service Bus and idempotent consumers, then uses the result to answer the training plan's system-design prompt: a quarterly billing run for 500,000 accounts.

## Learning objectives

By the end, the viewer can:

- Explain when `TransactionScope` promotes to a distributed transaction, and the state of distributed transactions in modern .NET.
- Implement a transactional outbox: business rows and outbox rows in one local transaction, and a dispatcher that publishes afterwards.
- Explain at-least-once delivery, and make every consumer idempotent with unique constraints and an inbox table.
- Configure Service Bus peek-lock processing for long-running work: lock duration, lock renewal, delivery count, dead-lettering.
- Replace the synchronous reporting write with events (or CDC), and define what reports show while a run is in progress.
- Answer the 500,000-account billing-run design question with numbers.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| What replaces a distributed transaction? | Local transactions plus eventual consistency: a transactional outbox for anything that must leave the database, idempotent consumers, and a compensating action or a visible "in progress" state where needed. Two-phase commit couples the availability of every participant and isn't available in Linux containers. |
| Explain the outbox pattern. | Write the state change and an outbox row in the **same local transaction**. A dispatcher reads unsent rows, publishes them, and marks them sent. A crash after publishing but before marking means the message is published again, so consumers must be idempotent. It solves the dual-write problem (database commit and publish can't be atomic). |
| At-least-once vs exactly-once? | Brokers deliver at least once; exactly-once end to end across a broker and a database isn't available. You get "effectively once" from at-least-once delivery plus idempotent processing: natural keys, unique constraints, an inbox table of processed message IDs. |
| How do you handle a message whose processing takes longer than the lock? | Auto lock renewal up to a maximum, or smaller messages (one per chunk) so each handler finishes well inside the lock. And design for the lock being lost anyway: the redelivered message must find its work already done. |
| Design the quarterly billing run for 500,000 accounts: overnight, resumable, auditable, never double-billed. | Idempotency key per run; outbox; partition by firm, then chunk; per-chunk local transactions; at-least-once with idempotent handlers; calculation traces stored with invoices; an approval step before fee debits go to custodians; throughput metrics and alerts; shadow mode for new engine versions; AUM snapshot versioning for late custodian files. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.BillingRunner/BillingRunnerService.cs` | `using (var scope = new TransactionScope())          // Billing DB + Reporting DB → MSDTC` |
| `legacy/FeeBilling.Data/Reporting/ReportingEntities.cs` | "Written by BillingRunner inside the same TransactionScope as the Invoices insert, which is why production needs MSDTC." |
| `database/reporting/001-schema.sql` | "Two connections in one scope = MSDTC promotion." `FeeFacts` has no natural unique key |
| `legacy/FeeBilling.BillingRunner/ProjectInstaller.cs` | "(MSDTC needs network access)" |
| `docker/servicebus/Config.json` | Queue `billing-runs`: `RequiresDuplicateDetection: false`, `LockDuration: PT1M`, `MaxDeliveryCount: 10` |
| `docker-compose.yml` | The Service Bus emulator (depends on the SQL Server container) |
| `src/FeeBilling.Billing.Api/`, `src/FeeBilling.Billing.Worker/` (future, WP-03 and WP-04) | Where the outbox write and the consumer live |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Show the `TransactionScope` and the reporting schema comment. One scope, two databases, one Windows service account. Ask: what happens to this in a Linux container? |
| 01:30–04:30 | Why MSDTC doesn't come with you | Promotion: a second connection (here, a second database) in a scope escalates to a distributed transaction. .NET 7+ supports distributed transactions **on Windows only**, and only after opting in with `TransactionManager.ImplicitDistributedTransactions = true`. On Linux it's simply unavailable. Even on Windows, 2PC means the billing run fails whenever reporting is down. Run the promotion demo. |
| 04:30–08:30 | The transactional outbox | The dual-write problem. The outbox table, the local transaction in `Billing.Api`, the dispatcher loop, the stable `MessageId`. Walk the crash points: before commit (nothing happened), after commit and before publish (dispatcher publishes later), after publish and before marking (published twice). |
| 08:30–12:00 | At-least-once and idempotency | Peek-lock, complete, abandon, lock expiry, `MaxDeliveryCount` → dead-letter queue. Read `Config.json`: duplicate detection is **off**, and even when on it only removes duplicate *sends* inside a time window; it doesn't stop redelivery. Idempotency lives in the database: unique `(RunId, AccountId)`, unique `(RunId, ChunkNo)`, and an inbox table. |
| 12:00–15:00 | A resumable run end to end | The sequence diagram below. `LockDuration` is one minute and a run takes many: set `MaxAutoLockRenewalDuration`, or send one message per chunk. Kill the worker mid-run and trace what happens to the lock, the redelivery and the completed chunks. |
| 15:00–17:00 | Reporting without 2PC | `InvoicesGenerated` events written through the outbox in the chunk's transaction, consumed into `FeeFacts` with an inbox. Alternative: CDC on `dbo.Invoices`, which makes the billing schema a public contract. Define the consistency contract: reports only show runs marked complete. |
| 17:00–18:30 | Shadow mode, briefly | The same pipeline with `Mode = Shadow` writes to `ShadowInvoices` while legacy stays the system of record; a nightly diff compares them. Details in video 23. |
| 18:30–20:00 | System design rehearsal and recap | The 500,000-account prompt with back-of-envelope numbers. Recap: local transactions, outbox, at-least-once, idempotent everything. |

### The outbox, in code

```sql
-- Expand-only (video 14)
CREATE TABLE dbo.OutboxMessages
(
    Id            bigint           IDENTITY(1,1) NOT NULL CONSTRAINT PK_OutboxMessages PRIMARY KEY,
    MessageId     uniqueidentifier NOT NULL CONSTRAINT UQ_OutboxMessages_MessageId UNIQUE,
    Type          varchar(100)     NOT NULL,
    Payload       nvarchar(max)    NOT NULL,
    CorrelationId varchar(64)      NULL,
    CreatedOn     datetime2(3)     NOT NULL,
    DispatchedOn  datetime2(3)     NULL
);
CREATE INDEX IX_OutboxMessages_Pending ON dbo.OutboxMessages (Id) WHERE DispatchedOn IS NULL;
```

```csharp
// Billing.Api: StartBillingRun handler (inside the execution strategy from video 15)
await using var tx = await db.Database.BeginTransactionAsync(ct);

var run = new BillingRun { FirmId = cmd.FirmId, PeriodEnd = cmd.PeriodEnd, Status = "Pending", IdempotencyKey = key };
db.BillingRuns.Add(run);
await db.SaveChangesAsync(ct);                                   // run.Id assigned

db.OutboxMessages.Add(OutboxMessage.For(new BillingRunRequested(run.Id, run.FirmId, run.PeriodEnd), clock));
await db.SaveChangesAsync(ct);

await tx.CommitAsync(ct);                                        // the run and its message exist together, or not at all
```

```csharp
// Dispatcher: a BackgroundService. Run one instance, or claim rows with READPAST as in video 15.
foreach (var message in pending)
{
    await sender.SendMessageAsync(new ServiceBusMessage(message.Payload)
    {
        MessageId = message.MessageId.ToString(),   // stable: resending the same row sends the same MessageId
        Subject = message.Type,
        ContentType = "application/json",
        CorrelationId = message.CorrelationId,
    }, ct);

    message.DispatchedOn = clock.GetUtcNow().UtcDateTime;
    await db.SaveChangesAsync(ct);                 // a crash before this line publishes the message again: allowed
}
```

### The consumer

```csharp
var processor = client.CreateProcessor("billing-runs", new ServiceBusProcessorOptions
{
    ReceiveMode = ServiceBusReceiveMode.PeekLock,
    AutoCompleteMessages = false,
    MaxConcurrentCalls = 1,
    MaxAutoLockRenewalDuration = TimeSpan.FromHours(1),   // the queue's LockDuration is PT1M; a run takes minutes
});

processor.ProcessMessageAsync += async args =>
{
    var request = args.Message.Body.ToObjectFromJson<BillingRunRequested>()!;
    await runner.RunAsync(request.RunId, args.CancellationToken);       // claims the run, skips completed chunks
    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
};

processor.ProcessErrorAsync += args =>
{
    logger.LogError(args.Exception, "Service Bus error on {EntityPath}", args.EntityPath);
    return Task.CompletedTask;
};

await processor.StartProcessingAsync(stoppingToken);
```

If the handler throws, the message is abandoned or its lock expires; either way it comes back. After 10 deliveries (`MaxDeliveryCount`) it's dead-lettered, which must raise an alert and mark the run `Failed`. A dead-letter queue nobody watches is a silent failure.

### Inbox for the reporting consumer

```csharp
await using var tx = await reporting.Database.BeginTransactionAsync(ct);

var firstTime = await reporting.Database.ExecuteSqlAsync($"""
    INSERT INTO dbo.ProcessedMessages (MessageId, ProcessedOn)
    SELECT {messageId}, SYSUTCDATETIME()
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ProcessedMessages WHERE MessageId = {messageId})
    """, ct);                                            // the primary key on MessageId is the real guard

if (firstTime == 1)
{
    reporting.FeeFacts.AddRange(evt.Lines.Select(ToFeeFact));
    await reporting.SaveChangesAsync(ct);
}

await tx.CommitAsync(ct);                                // then complete the Service Bus message
```

### A resumable run

```mermaid
sequenceDiagram
    participant UI as Browser
    participant API as Billing.Api
    participant DB as FeeBilling DB
    participant D as Outbox dispatcher
    participant SB as Service Bus
    participant W as Billing.Worker
    participant R as Reporting consumer

    UI->>API: POST /api/billing/runs (Idempotency-Key)
    API->>DB: BEGIN; INSERT run; INSERT outbox row; COMMIT
    API-->>UI: 202 Accepted { runId }
    D->>DB: read undispatched outbox rows
    D->>SB: send BillingRunRequested (MessageId = outbox MessageId)
    D->>DB: mark dispatched
    SB->>W: deliver (peek-lock, auto-renewed)
    loop each chunk not yet Complete
        W->>DB: BEGIN; INSERT invoices + traces; mark chunk Complete; INSERT outbox InvoicesGenerated; COMMIT
    end
    W->>DB: mark run Complete
    W->>SB: complete message
    D->>SB: send InvoicesGenerated
    SB->>R: deliver
    R->>R: inbox check, upsert FeeFacts, COMMIT, complete
```

`docker/servicebus/Config.json` defines only the `billing-runs` queue. `InvoicesGenerated` needs its own queue, or a topic if more than one consumer will subscribe.

## Demo

```bash
docker compose up -d          # SQL Server, Azurite, Service Bus emulator
dotnet run --project tools/FeeBilling.DbInit
cat docker/servicebus/Config.json
```

**Promotion, live (Windows).** Save this outside the repo (the repo's `Directory.Build.props` and central package management would otherwise apply to it) and run it with `dotnet run promote.cs`:

```csharp
#:package Microsoft.Data.SqlClient@6.1.7
using System.Transactions;
using Microsoft.Data.SqlClient;

const string server = "Server=localhost,1433;User Id=sa;Password=FeeBilling!Passw0rd;TrustServerCertificate=True";

using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
await using var billing = new SqlConnection(server + ";Database=FeeBilling");
await billing.OpenAsync();
await using var reporting = new SqlConnection(server + ";Database=FeeBillingReporting");
await reporting.OpenAsync();   // second connection in the scope: promotion
scope.Complete();
```

Expect an exception telling you implicit distributed transactions aren't enabled (`TransactionManager.ImplicitDistributedTransactions` defaults to `false` on .NET 10; verify the exact message before recording). Set it to `true` and the promotion then needs a working MSDTC on both ends. On Linux it fails regardless.

**Kill mid-run** (once WP-04 exists): start a run, stop the worker after a few chunks, restart it, show the message redelivered (`DeliveryCount` > 1 in the logs), completed chunks skipped, and the invoice count equal to the number of billable accounts. Connect to the emulator with the connection string from its documentation (it uses `UseDevelopmentEmulator=true`).

### Back-of-envelope for the 500,000-account prompt

| Input | Value | Note |
|---|---|---|
| Accounts | 500,000 across 60 firms | Partition by firm, then chunk |
| Chunk size | 500 billing units | 1,000 chunks |
| Throughput (assumed) | 100 accounts/s per worker, 4 workers | Measure yours in video 15 |
| Wall-clock | 500,000 ÷ 400 ≈ 21 minutes | Plenty of margin for an overnight window, and for one full retry |

## Traps to call out

- **"Just turn MSDTC back on."** On Windows you can opt in, but it ties the run to the availability of both databases and to Windows hosting, which is what the migration is trying to leave.
- **Publishing inside the database transaction.** Sending to Service Bus before `COMMIT` publishes messages for rows that may roll back. Sending after `COMMIT` without an outbox loses messages on a crash. The outbox exists because neither order works.
- **Trusting broker duplicate detection.** It's disabled on `billing-runs`, and even when enabled it only covers duplicate sends inside the detection window. Redelivery after a lost lock is the same message delivered again.
- **Lock expiry on long handlers.** A one-minute lock and a ten-minute run means a second worker receives the message while the first is still going. The claim and chunk idempotency from video 15 are what make that harmless.
- **An unwatched dead-letter queue.** After 10 failed deliveries a billing run just stops, silently, unless DLQ depth is alerted on.
- **Assuming reporting is correct mid-run.** With events, `FeeFacts` fills up chunk by chunk. Decide, and document, whether reports show in-progress runs.
- **Reaching for a messaging framework without checking the licence.** MassTransit, NServiceBus and Wolverine all offer outboxes. Some have moved to commercial licences (for example MassTransit from v9; verify before recording). A hand-rolled outbox is a few hundred lines you fully understand.

## Key terms

MSDTC · two-phase commit (2PC) · transaction promotion · dual-write problem · transactional outbox · inbox · at-least-once delivery · idempotent consumer · peek-lock · lock renewal · dead-letter queue · duplicate detection · eventual consistency · CDC · shadow mode

## After the video

1. Write ADR-0005's "No distributed transactions" principle as a full ADR: context (MSDTC, Linux), decision (outbox + idempotent consumers), consequences (eventual reporting, DLQ operations).
2. Draw the sequence diagram for a *failed* run: a chunk that throws on every delivery until it's dead-lettered. Who is alerted, and what does the run's status show?
3. Rehearse the 500,000-account design answer in five minutes, using the numbers you measured in video 15.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 6.2 (Transactions), Section 8 (principles 3 and 4), Section 9 WP-04, Section 12.3
- `docs/handover.md`: "It uses MSDTC, which isn't available where we want to host."
- Microsoft Learn: *Distributed transactions in .NET* (`TransactionManager.ImplicitDistributedTransactions`), *Azure Service Bus message transfers, locks, and settlement*, *Duplicate detection*, *Dead-letter queues*, *Azure Service Bus emulator*
- Chris Richardson, *Pattern: Transactional outbox* (microservices.io)
