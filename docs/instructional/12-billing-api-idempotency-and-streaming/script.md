# 12 · Billing API: Idempotency, CQRS and Streaming

Welcome. Two behaviours in FeeBilling's legacy billing endpoints look like bugs, but they're really design problems. First, starting a billing run has no duplicate protection anywhere, so a double-click queues two runs, and two runs mean double-billed clients. Second, the invoice endpoints load an entire run into memory, for firms with forty thousand accounts. This lesson designs the replacement Billing API: a start-run endpoint that stays idempotent under concurrent requests, vertical slices without a mediator library, errors as problem details, and invoice listing and CSV export that use constant memory.

## The questions this lesson answers

Here are the questions this lesson prepares you for. How do you make a POST idempotent? Isn't a unique constraint on firm and period enough? Offset or cursor paging? How do you export fifty thousand rows without loading them into memory? And do you need MediatR to do CQRS?

These come up constantly in system design interviews, and FeeBilling gives you a concrete, money-shaped reason for each answer.

## Why the double-click double-bills

Start with the legacy code path. Open `legacy/FeeBilling.Web/Controllers/Api/BillingController.cs`. The start-run action reads the user name from `HttpContext.Current`, calls `BillingRunService.Enqueue` with the firm ID and period end, and returns the new run ID. The comment at the end of the line says it all: double-click equals two billing runs equals double-billed clients.

Follow it down. `BillingRunService.Enqueue` calls the stored procedure `usp_EnqueueBillingRun`. Open `database/billing/002-stored-procedures.sql`, and the header for procedure five says: enqueue a run, no duplicate check. It inserts a pending row and returns the new identity. And the `BillingRunQueue` table in the schema has no unique constraint on firm and period either.

The front end knows about it. In `legacy/FeeBilling.Web/App/billing/billing-review.controller.js`, the start-run function has a comment, FB-247: people double-click this and we get two runs for the same period; server-side de-dupe is on the backlog.

So there's no protection at any layer: not in the UI, not in the API, not in the procedure, not in the table. And the obvious quick fix, disabling the button after the first click, doesn't solve it. It doesn't help with network retries, with proxies that retry on a timeout, or with two people on two machines starting the same run. Idempotency has to be enforced where the data is.

## Designing the Idempotency-Key flow

Here's the standard design. The client generates a unique key for each logical operation, say a GUID, and sends it in an `Idempotency-Key` header. The client sends the same key if it retries.

The server keeps a table of keys. Each row holds a scope, like StartBillingRun, the key itself, a hash of the request body, and the result, which here is the run ID. The primary key is scope plus key, so the database guarantees a key can only be stored once.

There are three outcomes. If the key is new, the server does the work and stores the key and the result. If the key exists with the same request hash, the server replays the stored result: same run ID, same response, no second run. And if the key exists with a different request hash, someone reused a key for a different request, and the server rejects it with a 422.

What exactly do you hash? Not the raw bytes of the body. Two logically identical requests can differ in whitespace or property order, and a byte hash would call them different. Hash a canonical form of the fields that define the operation: firm ID, period end and run type, written in a fixed order and a fixed format. Then a retry from a different client library still matches.

Keys don't live forever. Keep them long enough to cover any realistic retry, say a day for an interactive client or longer for batch integrations, and clean them up with a scheduled job. And scope them. If keys are global, a client could, by accident or on purpose, send another firm's key and get another firm's result back. Scope the lookup by the caller's tenant, so a key only ever replays a result the same tenant created.

The key must be stored in the same transaction as the side effect. If you save the key but not the run, a retry finds the key and returns a run that doesn't exist. If you save the run but not the key, a retry creates a second run. The key row, the run row, and the outbox row for the message that starts processing all commit together, in one local transaction.

## The race, and why only a constraint closes it

Now the part interviewers push on: what happens when two requests with the same key arrive at the same moment?

The naive handler checks first: look up the key, and if it's not there, insert. That has a race window. Both requests look, both see nothing, and both insert a run. Check-then-insert can never be made safe on its own.

The constraint closes it. Both requests look and see nothing, and both try to insert the key. The first insert takes the key. The second insert blocks on the first one's uncommitted row, and when the first transaction commits, the second fails with a duplicate-key error, which SQL Server reports as error 2627 or 2601. The handler catches exactly that error, rolls back, reads the winner's stored result, and returns it. Both callers get the same run ID, and there's one run.

What should the replay look like? The same status code and body as the original response, so the client can't tell the difference and doesn't need to. Some APIs add a response header saying the result was replayed, which helps with debugging. Blocking the second request until the first commits works here, because enqueuing a run is quick. For an operation that takes minutes, you'd store the key with an in-progress status first and return a conflict to a duplicate that arrives while the original is still running, rather than holding a database lock for minutes.

Prove it with a test. Use the same pattern as `AccountsApiFixture` in the existing test project: `WebApplicationFactory` for the API and Testcontainers for a real SQL Server. Fire ten concurrent POSTs with one key, and assert that there's exactly one run in the table and exactly one distinct run ID across the ten responses. Add a second test: same key, different body, expect a 422. A real database matters here, because the in-memory provider doesn't enforce the constraint behaviour you're testing.

## Why you still need a natural key

Isn't a unique constraint on firm and period enough? It's necessary, and it isn't sufficient.

It's necessary because the unmodified AngularJS app sends no idempotency header. Until that screen is rewritten, the only thing that can stop the double-click is a business-level uniqueness rule: one regular run per firm, period end and run type. That's a unique index on those three columns. RunType would be a new column, with values like regular, rerun and shadow, so a deliberate rerun is still possible.

It isn't sufficient because it can't give an API client a safe retry. A client that timed out wants the same response it would have got, and it wants the server to know the difference between "I'm retrying" and "I really do want another run." The idempotency key carries that intent. So you want both: the natural key for business uniqueness, and the idempotency key for retry safety.

Two practical warnings. Adding that unique index fails if production already contains duplicate runs, and given FB-247, it probably does. Clean the data first, as its own reviewed step. And if you use a filtered index, say one that ignores cancelled runs, be careful: inserts into a table with a filtered index require the quoted identifier setting to be on, and a stored procedure keeps the setting it was created with. If the legacy enqueue procedure was deployed with it off, legacy enqueues start failing the day your index ships. Check how the procedures were deployed first.

And don't rely on the message broker to de-duplicate for you. The `billing-runs` queue in `docker/servicebus/Config.json` has duplicate detection turned off, and even when it's on, it only covers a time window. Consumers must be idempotent too, which lesson sixteen covers.

## Slices, handlers and errors

Now the shape of the code. Organize the Billing API by feature, not by layer. A folder per use case: start billing run, get billing run, list invoices, preview fee, create fee schedule, get fee schedule. Each folder holds the request type, the handler, and the endpoint mapping. That's a vertical slice. When you change how a run starts, everything you need is in one place.

Commands change state, queries read it. That separation is CQRS in its simplest form, and it doesn't need a library. A handler interface with a single handle method, one implementation per command or query, registered in the container, and the minimal API endpoint injects the handler directly. That's about fifty lines. As of 2025, MediatR and AutoMapper moved to commercial licences, so check current terms. In a regulated vendor, every new dependency needs a licence review, and a pattern this small doesn't justify one.

For validation, use FluentValidation or hand-rolled validators. .NET 10 also adds built-in validation for minimal APIs; check its current capabilities before relying on it. Return errors as RFC 9457 problem details under the new versioned API. But not under the legacy route: `billing-review.controller.js` reads `response.data.Message` when starting a run fails. Under the legacy path, keep the legacy error shape.

That's the versioning story in one sentence. The new API lives under slash api slash v1 slash billing. The legacy path, api slash billing, stays alive through a YARP path transform, from lesson nine, until the AngularJS screens are gone. Anything that changes a response shape goes into v1 only.

The last slice is the preview. A what-if fee calculation runs the pure fee engine with no persistence. No state changes, so it's naturally idempotent and safe to retry. It's still a POST rather than a GET, because the request carries a hypothetical schedule and a set of balances, which don't belong in a URL. It's also the cheapest way for a product owner to see what a proposed schedule change would do to a client's fee before anyone saves it.

## Invoices without buffering

Now the second problem. Open `legacy/FeeBilling.Data/InvoiceRepository.cs`. `GetInvoiceDataSet` fills a whole `DataSet` from the stored procedure for the run, and the comment says: whole run in memory, forty thousand rows for the big firms. Then open `legacy/FeeBilling.Web/Controllers/Api/InvoicesController.cs`. Its list action calls `ToList` on every invoice in the run, with the comment "no paging; 40k rows for the large firms," and then reads each invoice's account number and name, with the comment "lazy load per invoice." That's forty thousand extra queries on top of the forty thousand rows.

For listing, use keyset paging, also called cursor paging. Offset paging, skip and take, gets slower as the offset grows, because the database still reads and discards every skipped row, so page eighty costs eighty pages of reads. It's also unstable: if rows are inserted between requests, the reviewer skips or repeats rows. Keyset paging asks for rows after the last one you saw: invoices for this run with an ID greater than the cursor, ordered by ID, take one page. That's an index seek every time. The existing index on the invoices table's run ID supports it, because SQL Server adds the clustered key, the invoice ID, to the keys of a non-unique non-clustered index.

Make the cursor opaque. The response carries a next-page token, which is really the last ID encoded, and the client passes it back without interpreting it. That leaves you free to change the sort key later without breaking clients. Cap the page size on the server, whatever the client asks for, so one request can't pull the whole run. And for a screen that shows fifty thousand rows, pair keyset paging with virtual scrolling in the front end, which lesson twenty-two covers.

For the CSV export, stream. Project to a narrow row type with just the columns the file needs. Enumerate with `AsAsyncEnumerable`, and write each row to the response stream as it arrives, with async I/O throughout. Memory stays flat, whether the run has four hundred invoices or forty thousand. One ASP.NET Core detail: synchronous I/O on the response stream is disallowed by default, so a stream writer that flushes synchronously when it's disposed will throw. Dispose it asynchronously.

And fix the formatting while you're there. The legacy `CsvExporter.FormatValue` calls `ToString` with a comment that it uses the server's current culture. But `Web.config` sets the globalization culture to auto, which means the thread culture follows the browser's Accept-Language header. So the CSV format depends on who downloaded it: a user with a French-Canadian browser gets 5312 comma 50 instead of 5312 point 50, and whatever imports that file downstream breaks. Use the invariant culture and document the format.

## Traps

The traps to call out.

Check-then-insert. Only a unique constraint closes the race window.

Storing the key outside the transaction. Key, run and outbox row commit together.

Filtered unique indexes and old stored procedures. Check the quoted identifier setting the procedures were created with.

Requiring the header on legacy routes. The AngularJS app doesn't send it, so the natural key protects legacy callers.

Relying on the message broker to de-duplicate.

Offset paging on a forty-thousand-row run.

Buffering "just this once." Calling `ToListAsync` before writing the CSV brings back the legacy memory profile, and the gateway's timeout will cut long buffered exports.

Culture-dependent CSV output.

And adding a library to get CQRS. The pattern is the separation, not the package.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do you make a POST idempotent?

[pause 5s]

The client sends an idempotency key header. The server stores the key, a hash of the request and the result, in the same transaction as the side effect, with a unique constraint on the key. A repeat with the same key and body replays the stored result. The same key with a different body gets a 422. Concurrent duplicates are resolved by the constraint: the loser gets a duplicate-key error, catches it, and returns the winner's result. And I prove it with a test that fires concurrent requests against a real database.

**Interviewer:** Isn't a unique constraint on firm and period enough?

[pause 5s]

It stops the double-click, and it's what protects the legacy AngularJS screen, which sends no key. But it can't give an API client a safe retry that returns the same response, and it can't tell a retry from a deliberate rerun. So I'd use both: a natural key on firm, period end and run type for business uniqueness, and an idempotency key for retry safety. And I'd clean up existing duplicate runs before adding the index.

**Interviewer:** Offset or cursor paging?

[pause 5s]

Cursor, or keyset, for anything large or changing. Offset paging gets slower as the offset grows, because skipped rows are still read, and it skips or duplicates rows when data changes between pages. Keyset paging asks for rows after the last ID seen, which is an index seek every time and stable under inserts. Offset is fine for small, static lists.

**Interviewer:** How do you export fifty thousand rows without loading them into memory?

[pause 5s]

Project to a narrow row type, enumerate with `AsAsyncEnumerable`, and write each row to the response stream as it arrives, with async I/O, including disposing the writer asynchronously. Format with the invariant culture. Memory stays flat regardless of size. The legacy export built a whole data set first, and its number format depended on the downloader's browser culture.

**Interviewer:** Do you need MediatR for CQRS?

[pause 5s]

No. CQRS is separating commands from queries. A handler interface and one handler per use case is about fifty lines, and minimal API endpoints can inject handlers directly. MediatR moved to a commercial licence in 2025, and in a regulated vendor every dependency needs a licence review, so I wouldn't add one for a pattern this small.

## Recap

Five things to remember from this lesson.

One: FeeBilling has no duplicate protection at any layer, which is why a double-click double-bills. Disabling the button doesn't fix retries.

Two: an idempotency key is stored with a request hash and the result, in the same transaction as the side effect, and the three outcomes are new, replay and mismatch.

Three: only a unique constraint closes the concurrent race; catch the duplicate-key error and return the winner's result. Pair it with a natural key on firm, period and run type for the legacy screen.

Four: keyset paging and streamed CSV export keep memory and query cost flat. Format with the invariant culture.

Five: vertical slices and a hand-rolled handler give you CQRS without a licensed library.

In the next lesson, we'll go from API design down to data access: what changes between Entity Framework 6 and EF Core, and which of those changes bite during a migration.
