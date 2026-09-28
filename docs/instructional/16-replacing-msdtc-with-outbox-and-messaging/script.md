# 16 · Replacing MSDTC: Outbox and Messaging

Welcome. In the last lesson we rebuilt the billing runner's host and made its batch processing resumable. This lesson tackles the other half: the distributed transaction. The legacy runner writes invoices to one database and reporting facts to another, inside one transaction scope, and that needs MSDTC. By the end of this lesson, you should be able to explain why that doesn't come with you to modern hosting, describe the transactional outbox in detail, explain at-least-once delivery and idempotent consumers, and answer a classic system design question: a quarterly billing run for five hundred thousand accounts.

## The questions this lesson answers

Here's what you'll be ready for. What replaces a distributed transaction? Explain the outbox pattern. At-least-once versus exactly-once. How do you handle a message whose processing takes longer than its lock? And the design prompt: design the quarterly billing run for five hundred thousand accounts across sixty firms. It must finish overnight, be resumable, be auditable, and never double-bill.

Every one of these gets the same follow-up question from a good interviewer: and what about duplicates? So we'll keep coming back to that.

## One scope, two databases

Open `legacy/FeeBilling.BillingRunner/BillingRunnerService.cs` and find the using statement that creates a new `TransactionScope`. The comment beside it says: Billing DB plus Reporting DB, arrow, MSDTC. Inside that scope, the runner adds invoices to the FeeBilling database through one context, and fee facts to the FeeBillingReporting database through another.

The reporting side confirms it. `legacy/FeeBilling.Data/Reporting/ReportingEntities.cs` says the reporting database is written by the billing runner inside the same transaction scope as the invoices insert, which is why production needs MSDTC. And the reporting schema script, `database/reporting/001-schema.sql`, says it in five words: two connections in one scope equals MSDTC promotion.

What's promotion? A `TransactionScope` starts as a lightweight local transaction. The moment a second connection, here to a second database, enlists in the same scope, the transaction is escalated to a distributed transaction, coordinated by the Microsoft Distributed Transaction Coordinator using two-phase commit. In phase one, the coordinator asks every participant to prepare. In phase two, if all of them said yes, it tells them to commit.

That has operational consequences you can see in the repository. `ProjectInstaller.cs` runs the service under a domain account, and its comment says MSDTC needs network access. Both databases must be up at the same moment for any run to succeed. And the handover says it plainly: the runner uses MSDTC, which isn't available where we want to host.

## Why MSDTC doesn't come with you

Here's the state of distributed transactions in modern .NET, as of September 2026. For years, .NET Core didn't support them at all. Starting with .NET 7, distributed transactions are supported on Windows only, and only if you opt in by setting `TransactionManager.ImplicitDistributedTransactions` to true. It defaults to false, so the legacy code, ported as-is, throws the moment it promotes. On Linux, and so in Linux containers, it's simply not available.

Could you turn it on and host on Windows? You could, and it's worth saying why you wouldn't. Two-phase commit couples the availability of every participant. If the reporting database is down for maintenance, billing can't run. It ties you to Windows hosting, which is part of what the migration is trying to leave. And it doesn't help with the other system you'll soon need to talk to, a message broker, which doesn't take part in a SQL Server distributed transaction.

So the target architecture's principle number four is: no distributed transactions. Anything that must leave the billing database goes through a transactional outbox.

## The transactional outbox

Start with the problem the outbox solves, called the dual-write problem. When a billing run is requested, two things must happen: the run is saved in the database, and a message is published so a worker picks it up. You can't make a database commit and a broker publish atomic.

Try both orders. Publish first, then commit: if the commit fails, you've published a message about a run that doesn't exist. Commit first, then publish: if the process crashes between the two, the run exists and no one is ever told. Neither order works.

The outbox fixes this by making the message part of the database transaction. You add an `OutboxMessages` table, with an identity column, a unique message ID, a type, a JSON payload, a correlation ID, a created time, and a dispatched time that starts null. When the Billing API handles a start-run request, it opens one local transaction, inserts the billing run, inserts an outbox row describing a `BillingRunRequested` message, and commits. The run and its message exist together, or not at all.

Then a separate process, the dispatcher, reads undispatched outbox rows, publishes each one to Azure Service Bus, and marks it dispatched. The dispatcher is a background service. Either run a single instance, or have instances claim rows atomically with the same READPAST technique from the last lesson.

Now walk the crash points, because this is where interviewers probe. A crash before the commit: nothing happened, and the client can retry. A crash after the commit but before publishing: the row is still undispatched, so the dispatcher publishes it when it recovers. A crash after publishing but before marking it dispatched: the dispatcher publishes it again. That's allowed. The outbox guarantees the message is published at least once, not exactly once.

One important detail: when the dispatcher sends, it sets the Service Bus message's `MessageId` to the outbox row's message ID. That's stable. Resending the same row sends the same message ID, which helps consumers recognise duplicates.

## At-least-once delivery and idempotency

Service Bus, like every serious broker, delivers at least once. Here's how that works in peek-lock mode, which is the mode you use for work that matters. The receiver gets the message and a lock on it. While the lock is held, no one else receives it. When processing succeeds, the receiver completes the message and it's deleted. If processing fails, the receiver abandons it, or the lock simply expires, and the message becomes available again with its delivery count increased. After a maximum number of deliveries, the message is moved to the dead-letter queue.

Now open `docker/servicebus/Config.json`, the configuration for the local Service Bus emulator from `docker-compose.yml`. It defines one queue, `billing-runs`. Three properties matter. `LockDuration` is one minute. `MaxDeliveryCount` is ten. And `RequiresDuplicateDetection` is false.

People often reach for duplicate detection as the answer to duplicates. It isn't. It's turned off here, and even when it's on, it only discards a second send of the same message ID within a time window. It does nothing about redelivery, where the same message comes back because a lock was lost or a handler failed. Redelivery is normal, so it has to be handled by the consumer.

That's what exactly-once really means in practice. Exactly-once delivery end to end, across a broker and a database, isn't something you can buy. What you build is effectively-once processing: at-least-once delivery plus idempotent handlers. An idempotent handler produces the same result whether it runs once or five times.

Idempotency lives in the database, not the broker. Use natural keys and unique constraints: one invoice per run and account, one chunk row per run and chunk number. And for consumers that don't have a natural key, use an inbox table. The reporting consumer is a good example. In one local transaction, it inserts the message ID into a `ProcessedMessages` table, which has the message ID as its primary key. If the insert affects one row, this is the first time, so it writes the fee facts. If it affects zero rows, it's a duplicate, so it skips the work. Then it commits and completes the message. The primary key is the real guard, even if two copies race.

## Long-running work and the lock

Here's the interview question about locks. The queue's lock duration is one minute. A billing run takes many minutes. What happens?

Without extra configuration, the lock expires while the first worker is still processing. Service Bus makes the message available again, and a second worker picks up the same run. There are two fixes, and you should mention both.

First, lock renewal. The `ServiceBusProcessor` can renew the lock automatically. You set `MaxAutoLockRenewalDuration` in the processor options, for example to one hour, along with peek-lock mode, auto-complete turned off, and a controlled number of concurrent calls. The handler runs the billing run and then calls `CompleteMessageAsync` explicitly.

Second, smaller messages. Instead of one message per run, send one message per chunk. Each handler then finishes well inside a one-minute lock.

And then say the part that shows experience: design for the lock being lost anyway. Renewal can fail, a process can pause, a network can drop. When a redelivered message arrives, it must find its work already done. That's exactly what the previous lesson built: the run is claimed atomically, completed chunks are skipped, and a unique index stops duplicate invoices.

What about failures that never succeed? After ten deliveries, the message is dead-lettered. That must raise an alert and mark the run as failed. A dead-letter queue nobody watches is a billing run that silently stops.

## A resumable run, end to end

Let's put the whole flow together.

A user clicks start run in the browser. The request carries an idempotency key. The Billing API opens a local transaction, inserts the run and an outbox row, commits, and returns 202 Accepted with the run ID. The dispatcher reads the outbox row, sends a `BillingRunRequested` message to the `billing-runs` queue, and marks the row dispatched.

The worker receives the message under peek-lock, with renewal. It claims the run. Then, for each chunk that isn't yet complete, it opens a local transaction, inserts the invoices and their calculation traces, marks the chunk complete, and inserts an outbox row for an `InvoicesGenerated` event, and commits. When all chunks are done, it marks the run complete and completes the message.

The dispatcher then publishes the `InvoicesGenerated` events. The reporting consumer receives them, checks its inbox, writes the fee facts, commits, and completes. Note that the emulator's configuration only defines the `billing-runs` queue, so the events need their own queue, or a topic if more than one consumer will subscribe.

Now kill the worker halfway through. The lock stops being renewed and expires. The message is redelivered with a higher delivery count. The new worker reclaims the run, skips every chunk already marked complete, and finishes the rest. The invoice count at the end equals the number of billable accounts. Nothing was duplicated, nothing was missed, and no distributed transaction was involved.

What did we give up? Immediate consistency between billing and reporting. The reporting table now fills up chunk by chunk, a little behind the invoices. So you have to define the consistency contract, and write it down: for example, reports only show runs that are marked complete. An alternative to events is change data capture on the invoices table, but that turns the billing schema into a public contract for the reporting team, so choose it deliberately.

The same pipeline also gives you shadow mode. Run the worker with its mode set to shadow, and it writes to a `ShadowInvoices` table while legacy stays the system of record. A nightly job compares the two. That's how the new engine earns trust before cutover, and lesson twenty-three covers it.

## The system design question

Now the design prompt. Design the quarterly billing run for five hundred thousand accounts across sixty firms. It must finish overnight, be resumable, be auditable, and never double-bill.

Structure your answer around the requirements.

Never double-bill: an idempotency key per run, so a double-click or a client retry returns the original run. The outbox, so the request and its message commit together. Idempotent handlers, with unique constraints on invoices per run and account.

Resumable: partition by firm, then split each firm into chunks of billing units, say five hundred. Each chunk commits its invoices and its "done" marker in one local transaction. At-least-once messaging with idempotent handlers means any crash just means redelivery and skipping completed chunks.

Overnight: do the arithmetic out loud. Five hundred thousand accounts in chunks of five hundred is a thousand chunks. Assume a worker processes a hundred accounts a second, and you run four workers. That's four hundred accounts a second, so five hundred thousand divided by four hundred is about twenty-one minutes. That leaves plenty of room in an overnight window, even for a full retry. Then say you'd measure the real throughput rather than trust the assumption.

Auditable: store a calculation trace with every invoice line, showing the AUM used, the tiers applied, the day-count basis and the rounding. Add an approval step before fee debits are sent to custodians, because a debit is money leaving a client's account.

Operable: metrics for run duration and accounts per second, alerts on failures and on dead-letter queue depth, and shadow mode for new engine versions.

And one question that shows domain depth: what happens when a custodian file arrives late, after the run started? Version the AUM snapshots, so every invoice records exactly which positions it was calculated from, and a late file creates a new snapshot rather than silently changing an existing invoice.

## Traps

A few traps to call out.

Just turning MSDTC back on. You can on Windows, but it couples the run to both databases and to Windows hosting.

Publishing inside the database transaction, or after it without an outbox. Neither order works, which is why the outbox exists.

Trusting broker duplicate detection. It's off on this queue, and it never covered redelivery.

Ignoring lock expiry on long handlers. A one-minute lock and a ten-minute run means a second worker gets the message.

An unwatched dead-letter queue. Alert on its depth.

Assuming reporting is correct mid-run. Define what reports show.

And reaching for a messaging framework without checking its licence. MassTransit, NServiceBus and Wolverine all offer outboxes, and some have moved to commercial licences, so check current terms. A hand-rolled outbox is a few hundred lines you fully understand.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** What replaces a distributed transaction?

[pause 5s]

Local transactions plus eventual consistency. Anything that must leave the database goes through a transactional outbox, consumers are idempotent, and where the business needs it, there's a visible in-progress state or a compensating action. Two-phase commit couples the availability of every participant, and in modern .NET it's only available on Windows after an explicit opt-in, so it's not an option in Linux containers.

**Interviewer:** Explain the outbox pattern.

[pause 5s]

It solves the dual-write problem: you can't commit to a database and publish to a broker atomically. So you write the state change and an outbox row in the same local transaction. A dispatcher reads unsent rows, publishes them with a stable message ID, and marks them sent. If it crashes after publishing but before marking, the message is published again, so consumers must be idempotent.

**Interviewer:** At-least-once or exactly-once?

[pause 5s]

Brokers deliver at least once. Exactly-once end to end across a broker and a database isn't available. You get effectively-once from at-least-once delivery plus idempotent processing: natural keys, unique constraints, and an inbox table of processed message IDs. Broker duplicate detection only removes duplicate sends in a time window. It doesn't stop redelivery.

**Interviewer:** How do you handle a message whose processing takes longer than the lock?

[pause 5s]

Automatic lock renewal up to a maximum duration, or smaller messages, like one per chunk, so each handler finishes well inside the lock. And I'd design for the lock being lost anyway: the redelivered message must find its work already done, through an atomic claim, completed chunks being skipped, and unique constraints.

**Interviewer:** Design the quarterly billing run for five hundred thousand accounts.

[pause 5s]

An idempotency key per run and an outbox, so a request and its message commit together. Partition by firm, then chunk by billing unit, around five hundred per chunk, with a local transaction per chunk that commits invoices and the "done" marker together. At-least-once messaging with idempotent handlers makes it resumable. At four workers doing a hundred accounts a second, that's about twenty-one minutes, which I'd confirm by measuring. Calculation traces are stored with invoices for audit, there's an approval step before fee debits go to custodians, metrics and alerts cover throughput and the dead-letter queue, shadow mode protects new engine versions, and AUM snapshots are versioned for late custodian files.

## Recap

Five things to remember from this lesson.

One: two connections in one transaction scope promote to a distributed transaction. In modern .NET that's Windows-only and opt-in, and two-phase commit couples every participant's availability.

Two: the outbox solves the dual-write problem. The state change and the message row commit in one local transaction, and a dispatcher publishes afterwards with a stable message ID.

Three: brokers deliver at least once. Effectively-once comes from idempotent consumers: unique constraints, natural keys and an inbox table, not broker duplicate detection.

Four: long handlers need lock renewal or smaller messages, and must survive a lost lock anyway. Watch the dead-letter queue.

Five: for the design question, cover the idempotency key, the outbox, partitioning and chunking, the throughput arithmetic, calculation traces, approval before debits, and alerting.

In the next lesson, we'll move custodian ingestion off WCF, keeping a frozen SOAP contract working with CoreWCF.
