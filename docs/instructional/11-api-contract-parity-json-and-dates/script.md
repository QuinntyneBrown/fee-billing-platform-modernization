# 11 · API Contract Parity: JSON and Dates

Welcome. When an endpoint moves from Web API 2 to ASP.NET Core behind the same URL, the unmodified AngularJS app keeps calling it. If the JSON shape changes even slightly, nothing throws. A property reads as undefined, a table renders empty, a date shows the wrong day. This lesson is about those silent breakages: the serializer differences that cause them, the date and time rules that prevent off-by-one days, and the contract tests that prove a migrated endpoint still speaks the same language. And FeeBilling already has one of these regressions in the code, so we'll diagnose a real one.

## The questions this lesson answers

Here are the questions this lesson prepares you for. What breaks silently when you move an endpoint from Web API 2 to ASP.NET Core? How do you prove an API contract didn't change? `DateTime` or `DateTimeOffset`, and when? And a diagnostic one: why did the dates shift by a day?

The single idea underneath all four answers is this: the contract is what the legacy endpoint actually emitted on the wire, not what the C# class looks like.

## Naming: the obvious difference, and its catch

Start with property names. Web API 2 serializes with Newtonsoft.Json, and with the default contract resolver, Newtonsoft keeps the C# property names exactly. C# properties are PascalCase, so the JSON is PascalCase: AccountNumber, not accountNumber. Open `legacy/FeeBilling.Web/App_Start/WebApiConfig.cs`. The comment says it directly: JSON only, default Newtonsoft contract resolver, PascalCase property names, and the AngularJS app is written against PascalCase.

ASP.NET Core serializes with System.Text.Json, and its web defaults camel-case property names. So a straight port changes AccountNumber to accountNumber, and every line of AngularJS that reads account dot AccountNumber gets undefined. No exception, no error in the log, just an empty column.

The team that migrated Accounts.Api knew this. In `src/FeeBilling.Accounts.Api/Program.cs`, the JSON options set `PropertyNamingPolicy` to null, which means "use the C# names as they are," with a comment explaining that the AngularJS app reads PascalCase. And there's a test protecting it: `GetAccount_UsesPascalCasePropertyNames` in `tests/FeeBilling.Accounts.Api.Tests/AccountsEndpointsTests.cs` fetches the raw JSON and asserts it contains AccountNumber and not accountNumber. That's the right pattern: assert on raw JSON.

Now the catch. Open `legacy/FeeBilling.Web/Controllers/Api/BillingController.cs`. When you start a billing run, the action returns an anonymous object with a single property, runId. The C# property name is runId, in camel case, because that's what the developer typed. Newtonsoft keeps the name, so this one response is camel case. The AngularJS code agrees: `legacy/FeeBilling.Web/App/services/api.service.js` has a comment on `startRun` saying it returns runId in camel case, and `billing-review.controller.js` reads `response.runId`.

The same controller mixes conventions. The list of runs is also built from an anonymous object, but its properties are copied from the entity, Id, FirmId, PeriodEnd, Status, plus two computed ones named InvoiceCount and TotalAmount, all PascalCase. And the approve action returns runId and approved, in camel case again. Three responses, one controller, two naming conventions, and the front end depends on every one of them exactly as written.

So a blanket rule of "make everything PascalCase" would break this screen. The rule isn't PascalCase. The rule is: reproduce whatever legacy actually emitted, one response at a time. That's why you capture real responses rather than reasoning about them.

## Shapes System.Text.Json won't reproduce

Some legacy shapes don't come from a naming policy at all.

The biggest one is the data set. The invoices endpoint for a billing run loads an ADO.NET `DataSet` from a stored procedure and returns it directly with `Request.CreateResponse`. Newtonsoft serializes a data set as an object with one property per table, named after the table. The table was never named, so it gets the default name, Table. The result is an object with a single property called Table, holding an array of invoice rows.

The front end depends on that. Open `legacy/FeeBilling.Web/App/billing/run-invoices.controller.js`. It sets `vm.invoices` to `data.Table`, or an empty list. And there's a comment, FB-238, warning that the server sends back the whole data set, so the rows are under Table, and if anyone names the data table on the server, this page silently shows nothing.

System.Text.Json doesn't support serializing `DataSet` or `DataTable`. In current versions it refuses rather than producing the Newtonsoft shape; check the exact behaviour on your version. Either way, you won't get the Table wrapper for free. Under the legacy route, the new endpoint needs an explicit response type: an object with a Table property holding the rows. Ugly, and correct. Under the new, versioned route, you design a proper paged shape.

Reference loops are the next difference. `WebApiConfig.cs` sets Newtonsoft's reference loop handling to ignore, which silently omits the property that would create a cycle, for example an invoice's account's invoices. System.Text.Json's closest option, `ReferenceHandler.IgnoreCycles`, writes null for the cycle instead of omitting it. Different output. The better answer is to stop serializing entities at all: project to DTOs, and cycles can't happen.

## Error contracts

Errors are part of the contract, and they're the part people forget to test.

Web API 2 returns errors in its own shape. A simple error has a Message property. A validation error from `BadRequest` with model state has Message plus a ModelState object whose keys are field names, like dto dot Code, each mapping to a list of messages. ASP.NET Core returns RFC 9457 problem details instead: title, detail, status, and for validation, an errors object.

Now look at `legacy/FeeBilling.Web/App/schedules/schedule-edit.controller.js`. When saving a schedule returns a 400, it checks for `response.data.ModelState`, and the comment spells out the Web API 2 shape it expects. It sets the error text from Message, and it walks ModelState to show each field's message. If the new endpoint returns problem details, none of those properties exist. The code falls back to a generic "Save failed" message. The user loses every validation message, and nothing anywhere logs an error.

So decide per route. Under legacy routes that the AngularJS app still calls, keep the legacy error shape. Under the new versioned API, use problem details. And put at least one 400 and one 404 case in your contract tests.

## Other serializer differences

A few more, quickly.

Enums. The database stores schedule types as the strings TIERED, BLENDED and FLAT, and that's what the legacy API emits. The domain's `FeeScheduleType` enum has members named Tiered, Blended and Flat. Serialize the enum with a string enum converter and you get Tiered, in mixed case. It looks the same in C#, and it breaks every JavaScript string comparison, and the legacy validation attribute that expects the capitalized values.

Input is more forgiving than output. With web defaults, System.Text.Json reads property names case-insensitively and accepts numbers written as strings. So requests from the old front end usually still bind. It's the responses that break.

Numbers. A C# decimal carries its scale, so a fee of 5312.50 goes out as 5312.50, trailing zero included. Your snapshots should include that, because money formatting is part of what the client sees.

And inputs from the browser deserve a test too. `schedule-edit.controller.js` converts rates between percent and fraction in JavaScript, multiplying by a hundred on load and dividing by a hundred on save. In JavaScript, 0.35 divided by 100 evaluates to 0.0034999999999999996. That's the number the server receives for a 0.35 percent tier. Decide where it's rounded, and test it with realistic input.

## Dates, and the regression already in the code

Now the regression. Put two files side by side.

In `legacy/FeeBilling.Web/Models/AccountDto.cs`, the mapping for `LastValuedAt` has a comment: last valued at is stored in UTC; mark it so the JSON carries a trailing Z and the browser converts it to local time; without this, users west of UTC see the next day's date after 8 p.m.; FB-288. The code calls `DateTime.SpecifyKind` with `DateTimeKind.Utc`.

In `src/FeeBilling.Accounts.Api/Accounts/AccountDto.cs`, the migrated version, the mapping simply copies `account.LastValuedAt`. The fix is gone. That's handover issue number three.

Here's the mechanism. A `DateTime` carries a Kind: Utc, Local or Unspecified. Values read from SQL Server come back as Unspecified, in EF6 and in EF Core, because a `datetime2` column has no time zone. Both serializers honour the Kind. Utc is written with a trailing Z. Local is written with an offset. Unspecified is written with no suffix at all. And a browser parsing an ISO date-time string with no suffix treats it as local time.

Now use the seed data in `database/seed/010-seed-q3-2026.sql`. The comment says last valued at is UTC: the valuation job for September 30 finished at 8:30 p.m. Eastern. The stored value is October 1 at 00:30. With the Z, a browser in Eastern time converts it back to September 30 at 8:30 in the evening. Without the Z, it shows October 1. One missing line, one day wrong, for every user west of UTC after 8 p.m.

Notice who's to blame: not the serializer. The bug is losing the Kind between the database and the DTO. So there are three levels of fix. The minimal fix restores parity in the DTO: call `SpecifyKind` with Utc again. The structural fix puts an EF Core value converter on UTC columns, so they always materialize with Kind Utc, and no DTO can forget. The design fix uses `DateTimeOffset` for instants like last valued at, which carries its offset explicitly, and `DateOnly` for dates with no time, like `OpenedOn` or a period end. But the design fix changes the wire format: a DateOnly is written as just the date, not the date plus midnight. So it belongs in a new API version, not under a legacy route.

Date-only values have the same trap in a quieter form. `OpenedOn` is a SQL `date` column, so it arrives as a `DateTime` at midnight with an Unspecified Kind, and goes out as a date with a midnight time and no suffix. The AngularJS screens format dates with the date filter, which works in the browser's local time. As long as nothing converts that midnight value to UTC on the way, the displayed date is right. The moment someone well-meaning marks every date as UTC, open dates start shifting back a day for users west of Greenwich. Instants and calendar dates are different things, and they need different types.

## Contract tests that would have caught it

How do you prove a contract didn't change? Capture it before you migrate. For every function in `api.service.js`, record the real legacy response, for success and for error cases, and commit it. Then snapshot-test the new endpoint against it, with volatile fields like IDs and timestamps scrubbed. A snapshot library such as Verify makes that easy. Assert the shape: property names, types, and date suffixes, not just status codes.

And add a targeted regression test for this bug: get account 1001, parse the raw JSON, and assert that `LastValuedAt` is exactly October 1, 2026, at 00:30, written with the trailing Z. Today that test fails. With the one-line fix, it passes.

What does capturing look like in practice? Run the legacy app with the seed data loaded, and call every function the front end uses. `api.service.js` is the checklist: the billing run list, a single run, start run, the run's invoices, the CSV export, approve, the invoice PDF, the fee schedule list, a single schedule, create and update schedule, the accounts list, a household, and an account's positions. Record one success case and the realistic failure cases for each: not found, validation failure, and not authorized. Save each response as a file named for the endpoint and the case. Don't forget the endpoints that don't return JSON. The CSV export's contract is its column order, its number and date formatting, and its file name; the PDF's contract is at least its content type.

If you can't run legacy, reconstruct the contract from both ends. The server code tells you what's emitted, and the AngularJS code tells you which fields are actually read. The fields that are read are the ones that must not change.

When there are several consumers, say the AngularJS app plus a partner integration, consider consumer-driven contracts. Each consumer publishes the expectations it relies on, and the provider's build verifies all of them. For a single front end that you control, captured snapshots are usually enough.

One warning about typed tests. The existing tests mostly call `GetFromJsonAsync` of `AccountDto`, deserializing into the same class the server serialized from. A test like that can't detect a naming or date-suffix change, because both sides share the mistake. Assert on raw JSON.

## Traps

The traps to call out.

"We set PascalCase, so the contract is preserved." Anonymous objects, data sets and error bodies each have their own shape.

Trusting typed client tests, which share the server's assumptions.

Only testing the happy path, when the front end's error handling depends on Message and ModelState.

Blaming the serializer for date bugs. The Kind was lost before serialization.

Switching to DateOnly or DateTimeOffset under a legacy route. A good design change and a breaking contract change at the same time.

Enum casing drift, which is invisible in C#.

And floating-point inputs from the browser, arriving as long binary fractions.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** What breaks silently when you move an endpoint from Web API 2 to ASP.NET Core?

[pause 5s]

Property casing, because Newtonsoft keeps C# names while ASP.NET Core defaults to camel case. Data set serialization, which System.Text.Json doesn't do, so the legacy Table wrapper disappears. Error body shape, Message and ModelState versus problem details. Enum representation. Reference-loop handling. And date Kind handling, which decides whether a trailing Z appears. None of these throw; the client just reads undefined. In FeeBilling, the date one already happened: the migrated account DTO lost the line that marked last valued at as UTC.

**Interviewer:** How do you prove an API contract didn't change?

[pause 5s]

Capture real legacy responses, including error cases, before migrating, and commit them. Then snapshot-test the new endpoint's raw JSON against them, with volatile fields scrubbed, asserting names, types and date formats, not just status codes. I write those contract tests before moving the endpoint. With several consumers, I'd consider consumer-driven contracts.

**Interviewer:** DateTime or DateTimeOffset?

[pause 5s]

A `DateTime` carries a Kind, and values read from SQL come back Unspecified, so the offset information is easy to lose. `DateTimeOffset` carries the offset explicitly, so it's the safer default for instants, like when an account was valued. For dates with no time, like an open date or a period end, `DateOnly`. Changing the type changes the JSON, so I'd do it in a new API version, and fix the legacy route with an explicit UTC Kind instead.

**Interviewer:** Why did the dates shift by a day?

[pause 5s]

The value was stored in UTC but serialized without a Z, because its Kind was Unspecified, so the browser parsed it as local time. The valuation that finished at 8:30 p.m. Eastern on September 30 is stored as 00:30 on October 1 UTC; without the Z, users west of UTC see October 1. The fix is to restore the UTC Kind, ideally with a value converter so it can't be forgotten, plus a contract test that asserts the Z.

## Recap

Five things to remember from this lesson.

One: the contract is what legacy emitted on the wire. The legacy start-run response is camel case because its anonymous property is, so a blanket PascalCase rule is wrong.

Two: System.Text.Json won't reproduce a data set's Table wrapper or Newtonsoft's loop handling, so under legacy routes you build explicit response types.

Three: error bodies are part of the contract. The schedule editor reads Message and ModelState, and silently loses validation feedback without them.

Four: dates break through a lost Kind, not through the serializer. Restore UTC, add a value converter, and move to DateTimeOffset and DateOnly only in a new version.

Five: prove parity with captured legacy responses and raw-JSON snapshot tests, including errors, written before the migration.

In the next lesson, we'll design the Billing API itself: idempotency keys, CQRS and streaming exports.
