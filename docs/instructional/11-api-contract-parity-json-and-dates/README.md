# 11 · API Contract Parity: JSON and Dates

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-03 (and every migrated endpoint) · **Prerequisites:** 07, 09

**Audio lesson:** [11-api-contract-parity-json-and-dates.mp3](11-api-contract-parity-json-and-dates.mp3) · [Transcript](script.md)

## Why this video exists

When an endpoint moves from Web API 2 to ASP.NET Core behind the same URL, the unmodified AngularJS app keeps calling it. If the JSON shape changes even slightly, nothing throws: a property reads as `undefined`, a table renders empty, a date shows the wrong day. FeeBilling already has one of these regressions. Legacy `AccountDto` marks `LastValuedAt` as UTC (fix FB-288), and the migrated Accounts.Api `AccountDto` dropped that line, which is handover issue #3. This video covers the serializer differences that cause silent breakage, the date-and-time rules that prevent off-by-one days, and the contract tests that prove a migrated endpoint still speaks the same language.

## Learning objectives

By the end, the viewer can:

- List the Newtonsoft.Json (Web API 2) vs System.Text.Json (ASP.NET Core) differences that change wire output: naming, `DataSet`, reference loops, error shapes, enums.
- Explain why setting `PropertyNamingPolicy = null` isn't enough to preserve a contract.
- Diagnose the `LastValuedAt` regression from the code and fix it both minimally and structurally.
- Choose between `DateTime` (with an explicit `Kind`), `DateTimeOffset` and `DateOnly`, and know which choice changes the wire format.
- Write contract tests that capture legacy responses and fail on shape changes, including error responses.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| What breaks silently when you move an endpoint from Web API 2 to ASP.NET Core? | Property casing (Newtonsoft keeps C# names; ASP.NET Core defaults to camelCase). `DataSet` serialization. Error body shape (`{ Message, ModelState }` vs ProblemDetails). Enum representation. Date `Kind` handling. Reference-loop handling. None of these throw; the client just reads `undefined`. |
| How do you prove an API contract didn't change? | Capture real legacy responses (success *and* error cases) and snapshot-test the new endpoint's shape against them. Write the contract tests before migrating the endpoint. Consumer-driven contracts if there are several consumers. |
| `DateTime` vs `DateTimeOffset`? | `DateTime` carries a `Kind` (Utc, Local, Unspecified) and loses offset information; values read from SQL come back `Unspecified`. `DateTimeOffset` carries the offset explicitly and is the safer default for instants. Use `DateOnly` for dates with no time (period ends, open dates). Changing the type changes the JSON, so do it in a new API version, not under a legacy route. |
| Why did the dates shift by a day? | The value was UTC but serialized without a `Z`, so the browser parsed it as local time. For users west of UTC, an 8:30 p.m. Eastern valuation stored as `00:30` UTC the next day displays as the next day's date. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Web/App_Start/WebApiConfig.cs` | Default Newtonsoft contract resolver = PascalCase; `ReferenceLoopHandling.Ignore` |
| `src/FeeBilling.Accounts.Api/Program.cs` | `PropertyNamingPolicy = null` and its comment |
| `tests/FeeBilling.Accounts.Api.Tests/AccountsEndpointsTests.cs` | `GetAccount_UsesPascalCasePropertyNames` |
| `legacy/FeeBilling.Web/Controllers/Api/BillingController.cs` | `return Ok(new { runId });` and `Request.CreateResponse(HttpStatusCode.OK, ds)` (a `DataSet`) |
| `legacy/FeeBilling.Web/App/services/api.service.js` | Comments: `{ runId: 123 } (camelCase...)`, `{ "Table": [ ... ] }` |
| `legacy/FeeBilling.Web/App/billing/run-invoices.controller.js` | `vm.invoices = data.Table \|\| [];` and the FB-238 warning |
| `legacy/FeeBilling.Web/App/schedules/schedule-edit.controller.js` | Reads `response.data.Message` and `response.data.ModelState`; converts percent to fraction in JavaScript |
| `legacy/FeeBilling.Web/Models/AccountDto.cs` | `DateTime.SpecifyKind(a.LastValuedAt.Value, DateTimeKind.Utc)` with the FB-288 comment |
| `src/FeeBilling.Accounts.Api/Accounts/AccountDto.cs` | `LastValuedAt = account.LastValuedAt,`: the fix is gone |
| `database/seed/010-seed-q3-2026.sql` | `LastValuedAt` is UTC: `'2026-10-01T00:30:00'` = Sep 30, 8:30 p.m. Eastern |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Put the two `AccountDto` files side by side. One line is missing. Show what the AngularJS account list displays for account 1001: October 1 instead of September 30. |
| 01:30–04:30 | Naming | Web API 2 + Newtonsoft default contract resolver keeps C# names (PascalCase). ASP.NET Core's `JsonSerializerDefaults.Web` camelCases. Accounts.Api sets `PropertyNamingPolicy = null` and has a test for it. Then the catch: legacy `StartRun` returns `new { runId }`, and the C# property name *is* `runId`, so legacy emits camelCase for that one response. A blanket "PascalCase everything" rule would break `billing-review.controller.js` (`response.runId`). The contract is whatever legacy actually emitted. |
| 04:30–07:30 | Shapes System.Text.Json won't reproduce | `DataSet`: Newtonsoft writes `{ "Table": [ ... ] }`; System.Text.Json refuses to serialize `DataSet`/`DataTable` (it throws in current versions; verify). `run-invoices.controller.js` reads `data.Table`, so the new endpoint needs an explicit DTO shaped `{ Table: [...] }` under the legacy route. `ReferenceLoopHandling.Ignore` omits the looping property; `ReferenceHandler.IgnoreCycles` writes `null` instead. Better: project to DTOs so there are no cycles. |
| 07:30–10:00 | Error contracts | Web API 2 errors: `{ "Message": "..." }` and `{ "Message": "...", "ModelState": { "dto.Code": [...] } }`. ASP.NET Core returns RFC 9457 ProblemDetails (`title`, `detail`, `errors`). `schedule-edit.controller.js` falls back to a generic message, so users lose validation feedback with no error anywhere. Decide per route: keep the legacy error shape under legacy routes, use ProblemDetails in `/api/v1`. |
| 10:00–12:00 | Other serializer differences | Enums: the domain has `FeeScheduleType.Tiered`, but the contract is `"TIERED"`; `JsonStringEnumConverter` would write `"Tiered"`. Deserialization: web defaults are case-insensitive and allow numbers as strings, so input is more forgiving than output. Unknown properties are ignored by both by default. Numbers: `decimal` values go out with their scale; include trailing zeros (`5312.50`) in the snapshots. |
| 12:00–16:00 | Dates | `DateTime.Kind`: SQL `datetime2` comes back `Unspecified` (in EF6 and EF Core). Serializers write `Utc` with `Z`, `Unspecified` with no suffix, `Local` with an offset. The serializer isn't the bug; the lost `SpecifyKind` is. Fixes: the minimal fix (restore parity in the DTO), the structural fix (an EF Core value converter so UTC columns always come back `Utc`), and the design fix (`DateTimeOffset` for instants, `DateOnly` for `OpenedOn`/`PeriodEnd`, in a new API version because `"2026-08-15"` isn't `"2026-08-15T00:00:00"`). |
| 16:00–19:00 | Contract tests | Capture legacy responses for every endpoint the AngularJS app calls, including 400/404 cases, and commit them. Snapshot-test the new endpoint (e.g. Verify) with volatile fields scrubbed. Assert the *shape* (property names, types, date suffixes), not just status codes. Add a regression test for `LastValuedAt` ending in `Z`. |
| 19:00–20:00 | Recap | The contract is what legacy emitted, not what the DTO class looks like. Capture it, test it, and change it only in a new version. |

### Before: the regression

```csharp
// legacy/FeeBilling.Web/Models/AccountDto.cs
// LastValuedAt is stored in UTC. Mark it so the JSON carries a trailing 'Z' and the
// browser converts it to local time. Without this, users west of UTC see the next
// day's date after 8 p.m. (FB-288)
LastValuedAt = a.LastValuedAt.HasValue
    ? DateTime.SpecifyKind(a.LastValuedAt.Value, DateTimeKind.Utc)
    : (DateTime?)null
```

```csharp
// src/FeeBilling.Accounts.Api/Accounts/AccountDto.cs
LastValuedAt = account.LastValuedAt,
```

### After: three levels of fix (sketches)

```csharp
// 1. Minimal: restore parity in the DTO mapping
LastValuedAt = account.LastValuedAt is { } valuedAt
    ? DateTime.SpecifyKind(valuedAt, DateTimeKind.Utc)
    : null,
```

```csharp
// 2. Structural: UTC columns always materialize as Kind=Utc (AccountConfiguration)
var utc = new ValueConverter<DateTime, DateTime>(
    v => v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

entity.Property(e => e.LastValuedAt).HasPrecision(0).HasConversion(utc);
```

```csharp
// 3. Contract test that would have caught it
[Fact]
public async Task GetAccount_LastValuedAt_IsSerializedAsUtc()
{
    var json = await _client.GetStringAsync("/api/accounts/1001", TestContext.Current.CancellationToken);

    using var document = JsonDocument.Parse(json);
    var lastValuedAt = document.RootElement.GetProperty("LastValuedAt").GetString();

    Assert.Equal("2026-10-01T00:30:00Z", lastValuedAt);
}
```

The design-level fix (`DateTimeOffset` for `LastValuedAt`, `DateOnly` for `OpenedOn`) changes the JSON, so it belongs in `/api/v1` once the AngularJS screens no longer depend on the legacy shape.

### Before: shapes the AngularJS app depends on

```javascript
// legacy/FeeBilling.Web/App/billing/run-invoices.controller.js
// The server sends back the whole DataSet, so the rows are under "Table" (FB-238).
// If anyone names the DataTable on the server this page silently shows nothing.
return api.getRunInvoices(runId).then(function (data) {
    vm.invoices = data.Table || [];
```

```javascript
// legacy/FeeBilling.Web/App/schedules/schedule-edit.controller.js
if (response.status === 400 && response.data && response.data.ModelState) {
    // Web API 2 shape: { Message: "...", ModelState: { "dto.Code": ["..."] } }
    vm.error = response.data.Message;
```

## Demo

```bash
docker compose up -d
dotnet run --project tools/FeeBilling.DbInit --reseed

# Existing contract test: casing is protected...
dotnet test tests/FeeBilling.Accounts.Api.Tests --filter "FullyQualifiedName~UsesPascalCasePropertyNames"

# ...but nothing protects the date. Show the diff that caused the regression:
git diff --no-index legacy/FeeBilling.Web/Models/AccountDto.cs src/FeeBilling.Accounts.Api/Accounts/AccountDto.cs
```

1. Add the `GetAccount_LastValuedAt_IsSerializedAsUtc` test above to `AccountsEndpointsTests` on a scratch branch and run it: it fails with `2026-10-01T00:30:00` (no `Z`).
2. Apply the minimal fix, rerun, and it passes.
3. In the browser console, show the parsing difference: `new Date("2026-10-01T00:30:00")` vs `new Date("2026-10-01T00:30:00Z")`, with the machine's time zone set to Eastern.
4. Show the JavaScript percent conversion in `schedule-edit.controller.js`: `0.35 / 100` evaluates to `0.0034999999999999996`. That's the number the server receives for a 0.35% tier. Contract tests should include realistic *inputs* too.

## Traps to call out

- **"We set PascalCase, so the contract is preserved."** Anonymous objects, `DataSet`s and error bodies each have their own shape. The legacy output is the specification.
- **Trusting typed client tests.** Deserializing into the same DTO class you serialize from (as `GetFromJsonAsync<AccountDto>` does) can't detect a naming or date-suffix change. Assert on raw JSON.
- **Only testing the happy path.** The AngularJS error handling depends on `Message` and `ModelState`. Error shapes are part of the contract.
- **Blaming the serializer for date bugs.** Both serializers honour `DateTime.Kind`. The bug is losing the `Kind` between the database and the DTO.
- **Switching to `DateOnly` or `DateTimeOffset` under a legacy route.** It's a correct design change and a breaking contract change at the same time.
- **Enum casing drift.** `"TIERED"` vs `"Tiered"` is invisible in C# and breaks JavaScript string comparisons and the legacy `RegularExpression("TIERED|BLENDED|FLAT")` validation.
- **Floating-point inputs from the browser.** Rates computed in JavaScript arrive as long binary fractions. Decide where they're rounded, and test it (video 13).

## Key terms

Wire contract · naming policy · contract resolver · ProblemDetails (RFC 9457) · snapshot / approval testing · consumer-driven contract · `DateTime.Kind` · `DateTimeOffset` · `DateOnly` · value converter

## After the video

1. For every function in `api.service.js`, record the legacy response shape (success and error) that the AngularJS code actually reads. That list is the contract test backlog for WP-03.
2. Write the fix for handover issue #3 with a regression test, and decide whether it belongs in the DTO or the EF Core model.
3. Draft the `/api/v1` contract for accounts with `DateTimeOffset` and `DateOnly`, and note which AngularJS screens would need to change to use it.

## References

- `docs/handover.md`: known issue #3
- `docs/brasswick-modernization-training-plan.md`: WP-03 traps (Newtonsoft vs System.Text.Json, `DataSet` JSON), Section 12.2 ("What breaks silently")
- Microsoft Learn: *Migrate from Newtonsoft.Json to System.Text.Json*, *Preserve references and handle circular references*, *Handle errors in ASP.NET Core APIs* (ProblemDetails)
- Microsoft Learn: *Choose between DateTime, DateOnly, DateTimeOffset, TimeSpan, TimeOnly, and TimeZoneInfo*
- RFC 9457, *Problem Details for HTTP APIs*
- Verify (snapshot testing library) documentation
