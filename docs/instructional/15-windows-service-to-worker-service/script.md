# 15 · Windows Service to Worker Service

Welcome. This lesson is about the component that produces every invoice FeeBilling sends: the billing runner. It's a Windows Service, and it's the riskiest code in the solution. By the end of this lesson, you should be able to review it and name its bugs, including the ones its comments don't mention, and then describe the modern replacement: a .NET Worker Service that claims work safely, processes it in chunks with bounded parallelism, stops gracefully, and resumes after a crash without double-billing anyone. This lesson covers hosting and batch processing. The next one replaces the distributed transaction.

## The questions this lesson answers

Here's what you'll be ready for. How would you replace a Windows Service in modern .NET? Design a resumable batch job. How do you use a `DbContext` in parallel? How do you stop two instances from processing the same work? And what happens to in-flight work during a deploy?

The trap in all of these is porting the timer. An interviewer asking how you'd replace a Windows Service wants to know whether you'll just move the same loop into a new host, or redesign the job so it can be claimed safely, processed in parallel, stopped and resumed. Porting gets you a modern host with the same bugs.

## Reading the legacy runner

Open `legacy/FeeBilling.BillingRunner/BillingRunnerService.cs`. It derives from `ServiceBase`, the classic Windows Service base class. Its `OnStart` method does three things. It sets `HttpContext.Current` to a fake context created by `FakeHttpContext.Create`, with the comment "so FeeCalculator works outside IIS". It creates a `System.Timers.Timer` with an interval of thirty thousand milliseconds. And it wires the timer's Elapsed event to a method called `ProcessQueue`.

Why the fake HTTP context? Because the static fee calculators get their database context from `DbContextFactory.Current`, which lives in `legacy/FeeBilling.Data/DbContextFactory.cs`. It stores one `FeeBillingEntities` per request in `HttpContext.Current.Items`. Outside IIS there is no request, so the runner invents one. Keep that in mind, because it causes a bug in a minute.

The rest of the project is period detail. `Program.cs` has a debugging mode: run the executable with a double-dash console argument and it runs interactively instead of as a service. `ProjectInstaller.cs` is installed with `installutil`, and it runs the service under a domain account, with the comment that MSDTC needs network access.

Now `ProcessQueue`. It reads the next run with `FirstOrDefault` where status equals Pending. It loads every account for the firm. It opens a `TransactionScope` that spans the billing database and the reporting database. Inside, it uses `Parallel.ForEach` over the accounts, calculates each fee, and adds an invoice and a reporting fee fact. Then it saves both contexts and completes the scope. And after the scope closes, it sets the run's status to Complete and saves again. The comment on that line is the famous one: crash at account 40,000 means the whole run rolls back and starts over.

That comment is honest, but it's optimistic. Let's list what's actually wrong, because none of it shows up in the MSTest suite.

## The bugs the comments don't mention

First, overlap. `System.Timers.Timer` raises Elapsed on the thread pool every thirty seconds, and there's no reentrancy guard. A run for a large firm takes much longer than thirty seconds. So the next tick fires while the first run is still processing, and it finds the same run, because the run is still Pending.

Second, claiming. The run is never marked Running, and the `StartedOn` column in `BillingRunQueue` is never set. Nothing stops a second tick, or a second instance of the service, from picking the same run. And `FirstOrDefault` has no ordering, so which pending run goes first is undefined. There's a stored procedure, `usp_GetPendingBillingRun`, that orders by request time, and its comment says the runner uses a LINQ query instead and the procedure was kept "just in case".

Third, failure handling. There's no try-catch. A failing run stays Pending and is picked up again every thirty seconds, forever. The `ErrorMessage` column and the `usp_CompleteBillingRun` procedure exist, but nothing uses them. This timer's documentation also says it swallows exceptions thrown from Elapsed handlers, which is worth checking for your runtime, so the failure may never surface at all.

Fourth, context lifetime. The two contexts, `_db` and `_reportingDb`, are fields created when the service starts and live as long as the service. The change tracker grows run after run, and tracked entities can return stale values.

Fifth, the shared context. Inside `Parallel.ForEach`, each pool thread sets `HttpContext.Current` to the same fake context, with a comment referencing ticket FB-402: pool threads have no HTTP context either. That fixed the null reference, but it means every thread's `DbContextFactory.Current` returns the same `FeeBillingEntities`. The fee calculators run concurrently against one context. That's the symptom fixed, not the cause.

And sixth, the one that matters most. The invoices commit inside the scope. The status update happens after it. If the process dies between completing the scope and the final save, the invoices are committed and the run is still Pending. On the next tick, the firm gets billed again. And nothing in the schema prevents it: the `Invoices` table in `database/billing/001-schema.sql` has no unique key across run and account. So the comment says a crash restarts the run. In the wrong millisecond, a crash double-bills a firm.

A small one to finish: `DateTime.Now` writes local server time into `CompletedOn` and into the reporting table's `LoadedOn`.

## The Worker Service host

Now the replacement. In modern .NET, a long-running background process is a Worker Service. You create the host with `Host.CreateApplicationBuilder`, register services, and add your worker with `AddHostedService`. The worker derives from `BackgroundService` and overrides one method, `ExecuteAsync`, which receives a stopping token.

You get the same things an ASP.NET Core app gets: dependency injection, configuration and the options pattern, `ILogger`, and a host that manages startup and shutdown. The template is `dotnet new worker`.

Does it still run as a Windows Service? It can. Call `AddWindowsService` on the services collection, with the service name. When the process is started by the Windows Service Control Manager, it integrates with it. When it isn't, the call does nothing, so the same binary runs in a console, in a container, or as a service. If you're hosting in containers, you don't need it at all.

Replace the timer with `PeriodicTimer`. The pattern is a do-while loop: do the work, then await `WaitForNextTickAsync` with the stopping token. The key property is that ticks can't overlap. The next wait only starts after the work finishes. Inject `TimeProvider` rather than using the system clock directly, so tests can control time.

Graceful shutdown works through the stopping token. When the host is asked to stop, during a deploy or a scale-in, it signals the token and gives running work `HostOptions.ShutdownTimeout` to finish. The default is short, thirty seconds as of recent versions, so configure it for your workload, and make sure the container orchestrator's grace period is at least as long. And note one behaviour change from older versions: an unhandled exception in a `BackgroundService` now stops the host by default instead of being silently swallowed. That's what you want in a billing system.

But notice what `PeriodicTimer` doesn't fix. It prevents overlap within one process. It does nothing about two instances. Only an atomic claim does.

## Claiming work atomically

To claim work safely, the "find a pending run" and "mark it as mine" steps must be one atomic operation. Never select and then update, because two instances can both select the same row before either updates it.

In SQL Server, the standard pattern is a single UPDATE with an OUTPUT clause, against the top one pending row ordered by request time, using three table hints: `ROWLOCK`, `UPDLOCK` and `READPAST`. UPDLOCK takes an update lock on the row it reads, so nobody else can claim it. READPAST tells other sessions to skip rows that are locked, instead of waiting for them. The update sets the status to Running and sets `StartedOn`, and the OUTPUT clause returns the run's ID, firm and period end. If you run that statement in two query windows at the same time, the second window immediately gets the next run, not the same one.

An alternative is a conditional status transition: update the row where the ID matches and the status is still Pending, and check that exactly one row was affected. A row version column gives you the same guarantee with optimistic concurrency.

Why stress this? Because select-then-update is worse than it looks. Under read committed snapshot isolation, which is on by default in Azure SQL Database, readers don't block on writers. So both instances read Pending and both proceed.

One more piece: claims need an expiry. A worker that dies after claiming leaves its run marked Running forever. Add a lease, a claimed-until time that the worker extends with a heartbeat, so another instance can reclaim abandoned work. Or, once messaging arrives in the next lesson, let message redelivery drive the retry.

## Chunks, contexts and bounded parallelism

Now the batch itself. The core idea: persist the plan before you process it.

When a run starts, split the firm's billing units into chunks, say five hundred at a time, and write them to a chunk table keyed by run ID and chunk number, with a status and a completed time. Chunk membership is decided once and stored. If you recompute it after a restart, accounts that opened or closed in the meantime move between completed and pending chunks.

What's a billing unit? It's not always an account. Household billing needs every member's AUM to calculate the household fee and allocate it back. So the unit is either an account with its own schedule, or a whole household. If you chunk by account, household members land in different chunks.

Each chunk then runs in its own local transaction with its own `DbContext`. Register the context with `AddDbContextFactory`, inject `IDbContextFactory<BillingDbContext>`, and create one short-lived context per chunk. Never share one across threads. A `DbContext` isn't thread-safe. EF6 sometimes let the legacy code get away with it. EF Core detects concurrent use and throws an `InvalidOperationException` saying a second operation was started on this context instance before a previous operation completed. When you see that, don't blame EF Core. It's reporting a bug the legacy code always had.

For parallelism, use `Parallel.ForEachAsync` over the pending chunks, with a `ParallelOptions` that sets `MaxDegreeOfParallelism` from configuration and passes the cancellation token. Bounded parallelism protects the database from being flooded.

Inside each chunk: load the inputs set-based, in a few queries per chunk, not a few per account. Run the pure fee engine. Add the invoices and save. Then mark the chunk Complete, using `ExecuteUpdateAsync`, and commit. The invoices and the "chunk done" marker commit together, in one local transaction. That's the heart of resumability.

There's one EF Core detail that trips people up. If you enable connection resiliency with `EnableRetryOnFailure`, which you should for Azure SQL, EF Core refuses to let you begin a transaction yourself unless the work runs inside an execution strategy. So you call `CreateExecutionStrategy` on the database facade and put the whole transaction inside its `ExecuteAsync`. The lambda might run more than once, so clear the change tracker at the start and make sure it's safe to repeat.

## Resumability, and the acceptance test

Now kill the process in the middle of a run. What happens?

Every chunk that committed has its invoices and its Complete marker. The chunk that was in flight rolls back as a unit: no invoices, still Pending. On restart, the worker reclaims the run and skips completed chunks. Nothing is duplicated and nothing is missing.

Add a safety net in the schema: a unique index on invoices across run ID and account ID. It's an expand-only change, as lesson fourteen described, so check the existing data for duplicates before creating it. With that index, a replayed chunk fails loudly instead of silently double-billing.

There's one subtle failure the index is especially good for. A connection can drop during commit, and the client can't tell whether the commit succeeded. A retry then inserts again. The unique index turns that into a clear error, and the handler can check: if the chunk is already Complete, treat the violation as success.

The acceptance test from the work package is concrete. Start a run for the five hundred generated accounts of the Laurentien firm. Cancel the host after a few chunks commit. Start it again. Then assert two things: the count of invoices for the run equals the count of distinct account IDs, and every billable account has exactly one invoice.

And then measure performance. The legacy runner makes at least three database round trips per account: finding the account, loading the schedule with its tiers, and calling `usp_GetBillableAum`. The chunked design loads inputs in bulk. The target in the work package is fifty thousand accounts in under ten minutes on a local machine. Measure it, and report the number. Interviewers love a measured number.

## Traps

A few traps to call out.

Porting the timer. `PeriodicTimer` fixes overlap in one process, not across instances.

Select-then-update claiming. Under read committed snapshot, both instances proceed.

Claims with no expiry. A dead worker leaves its run Running forever.

Chunking by account when billing is by household. Chunk by billing unit.

Re-slicing on restart. Persist chunk membership once.

Beginning a transaction with retry-on-failure enabled, outside an execution strategy. EF Core throws, and the lambda must be safe to re-run.

Shutdown timeouts that don't match between the host and the orchestrator. The in-flight chunk rolls back, which is only safe because chunks are idempotent.

And blaming EF Core for the concurrency exception. It's telling you the truth.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How would you replace a Windows Service in modern .NET?

[pause 5s]

With a Worker Service: `Host.CreateApplicationBuilder` and a `BackgroundService`, with dependency injection, options, logging and graceful shutdown through the stopping token. If it must stay a Windows Service, I'd call `AddWindowsService`, which does nothing when it isn't running as one, so the same binary also runs in a container. But the host is the easy part. I'd fix the design too: atomic claiming, persisted chunks, one context per chunk, and idempotent writes, so a run can be stopped and resumed.

**Interviewer:** Design a resumable batch job.

[pause 5s]

Persist the plan before processing: split the work into chunks and store them with a status. Each chunk commits its output and its "done" marker in the same local transaction. On restart, skip completed chunks. Add unique constraints, like one invoice per run and account, so a replayed chunk fails loudly instead of duplicating. Cancellation or a crash rolls back only the chunk in flight.

**Interviewer:** How do you use a DbContext in parallel?

[pause 5s]

You don't share one. A `DbContext` isn't thread-safe, and EF Core detects concurrent use and throws. I'd use `IDbContextFactory` to create one short-lived context per unit of work, and bound the parallelism with `Parallel.ForEachAsync` and a maximum degree of parallelism. The legacy runner shared one context across `Parallel.ForEach`, so EF Core's exception is exposing an old bug.

**Interviewer:** How do you stop two instances processing the same work?

[pause 5s]

An atomic claim. In SQL Server, a single UPDATE with OUTPUT, using the UPDLOCK and READPAST hints, that marks the next pending row as Running and returns it. Or a conditional update checked by row count or row version. Never select and then update, especially under read committed snapshot. And add a lease or heartbeat so work claimed by a crashed instance can be reclaimed.

**Interviewer:** What happens to in-flight work during a deploy?

[pause 5s]

The host signals the stopping token and gives work the shutdown timeout to finish, and the orchestrator's grace period has to be at least that long. Anything not committed rolls back and is retried after the restart. That's only safe because each chunk commits atomically and the writes are idempotent.

## Recap

Five things to remember from this lesson.

One: the legacy runner's worst bug isn't the one in its comment. A crash between committing invoices and updating the status double-bills a firm, and nothing in the schema stops it.

Two: a Worker Service with `BackgroundService` and `PeriodicTimer` gives you a modern host with no overlapping ticks, and `AddWindowsService` keeps the Windows option open.

Three: claim work atomically with a single UPDATE and OUTPUT, using UPDLOCK and READPAST, and give claims a lease.

Four: persist chunks, give each chunk its own context and its own local transaction, commit the output and the "done" marker together, and bound the parallelism.

Five: resumability is proven with a kill-and-restart test, protected by a unique index, and performance is proven by measuring a real number.

In the next lesson, we'll remove the distributed transaction from the billing run, and replace it with a transactional outbox and messaging.
