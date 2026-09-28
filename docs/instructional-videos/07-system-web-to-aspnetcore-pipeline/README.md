# 07 · From System.Web to the ASP.NET Core Pipeline

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-03 · **Prerequisites:** 01, 02

## Why this video exists

`System.Web` doesn't exist in modern .NET, so every piece of the legacy web app that depends on it (`Global.asax`, HTTP modules, Web API 2 controllers, `HttpResponseMessage`, `HttpContext.Current`) has to be re-expressed in the ASP.NET Core pipeline. Interviewers test whether you know the mapping *and* the behaviours that change along the way. FeeBilling has already done this once: `Accounts.Api` replaced two Web API 2 controllers with minimal APIs. This video uses that as the reference, then maps the three controllers still waiting (`FeeSchedulesController`, `BillingController`, `InvoicesController`).

## Learning objectives

By the end, the viewer can:

- Map `Global.asax` events, HTTP modules and handlers, MVC 5 filters and Web API 2 constructs to their ASP.NET Core equivalents.
- Explain why middleware order is explicit in `Program.cs`, and what that changes compared to the IIS integrated pipeline.
- Port a Web API 2 action to a minimal API endpoint with `TypedResults` and `Results<...>`, and know what to do with `HttpResponseMessage`.
- Explain how model binding and validation responses differ, and why that can break an existing client.
- Replace a global `AuthorizeAttribute` with endpoint authorization or a fallback policy, and `HttpContext.Current.User` with an injected `ClaimsPrincipal`.
- Make and defend the controllers-vs-minimal-APIs decision.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| What replaces `Global.asax`, HTTP modules and handlers? | `Program.cs`: service registration on the builder, then an explicitly ordered middleware pipeline. Modules become middleware; handlers become endpoints. `Application_Error` becomes `UseExceptionHandler` + ProblemDetails. `Application_EndRequest` cleanup disappears into DI scope disposal. |
| Minimal APIs or controllers? | Both are fully supported. Minimal APIs: less ceremony, `TypedResults` give accurate OpenAPI metadata, good fit for small vertical slices. Controllers: familiar to a Web API 2 team, rich conventions and filters. Pick one per service and be consistent; FeeBilling's first slice chose minimal APIs. |
| What happens to `HttpResponseMessage` and `IHttpActionResult`? | No direct equivalent. The Web API compatibility shim was removed in ASP.NET Core 3.0. Return `IResult` / `TypedResults` (or `ActionResult<T>` in controllers). Returning an `HttpResponseMessage` from ASP.NET Core doesn't send that response; it gets serialized like any other object. |
| How does model binding differ? | Web API 2 binds simple types from the URI and one complex type from the body. ASP.NET Core infers sources by attribute and convention, and `[ApiController]` returns an automatic `ValidationProblemDetails` 400. Different error shapes can break clients that parse errors. |
| How do you get the current user without `HttpContext.Current`? | Pass it in: minimal APIs bind `ClaimsPrincipal` directly, controllers have `User`. `IHttpContextAccessor` exists but should be rare, and never in domain code. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Web/Global.asax.cs` | `Application_Start` registrations, `Application_EndRequest` disposing the per-request context, `Application_Error` logging |
| `legacy/FeeBilling.Web/App_Start/WebApiConfig.cs` | Attribute routes, XML formatter removed, `config.Filters.Add(new AuthorizeAttribute())` |
| `legacy/FeeBilling.Web/Web.config` | `<globalization culture="auto" .../>`, `<sessionState mode="InProc" .../>`, `<system.webServer><modules>` with `SystemWebAdapterModule` |
| `legacy/FeeBilling.Web/Controllers/Api/AccountsController.cs` | The migrated original, kept for rollback |
| `src/FeeBilling.Accounts.Api/Accounts/AccountsEndpoints.cs` | Its replacement: `MapGroup`, `RequireAuthorization`, `TypedResults`, `Results<Ok<AccountDto>, NotFound>` |
| `src/FeeBilling.Accounts.Api/Program.cs` | The pipeline: `UseExceptionHandler`, `UseAuthentication`, `UseAuthorization`, `UseSystemWebAdapters` |
| `legacy/FeeBilling.Web/Controllers/Api/BillingController.cs` | `HttpContext.Current.User`, `HttpResponseMessage`, `Request.CreateResponse(..., ds)` |
| `legacy/FeeBilling.Web/Controllers/Api/FeeSchedulesController.cs` | Unity-injected context, `ModelState`, `[Authorize(Roles = "ScheduleEditor")]`, `CreatedAtRoute` |
| `legacy/FeeBilling.Web/App/billing/billing-review.controller.js` | The client reads `response.data.Message` on errors |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Legacy `AccountsController.GetAccount` and `AccountsEndpoints.GetAccount` side by side. Same route, same JSON, completely different hosting model. What changed, and what had to stay the same? |
| 01:30–05:00 | `Global.asax` → `Program.cs` | `Application_Start` registrations become `builder.Services` calls. `Application_EndRequest` disposing the context from `HttpContext.Items` becomes a scoped `DbContext` disposed by DI. `Application_Error` + log4net becomes `AddProblemDetails()` + `UseExceptionHandler()`, which also logs. |
| 05:00–08:00 | Modules and handlers → middleware | The IIS integrated pipeline raised events in a fixed order; modules subscribed. ASP.NET Core runs middleware in the order you write it. Order bugs are now your bugs: `UseAuthentication` before `UseAuthorization`, exception handler first. Two `Web.config` behaviours that silently vanish: `culture="auto"` (thread culture from `Accept-Language`) only exists if you add request localization (video 18); `InProc` session only exists if you add session, and the legacy one isn't shared (video 10). |
| 08:00–11:30 | Web API 2 → minimal APIs | `RoutePrefix` → `MapGroup`; `IHttpActionResult` → `TypedResults` with `Results<Ok<T>, NotFound>` so the return types are visible to OpenAPI and tests. `HttpResponseMessage` and `Request.CreateResponse` have no equivalent: `BillingController.GetInvoices` must be redesigned (CSV as a file or stream result, video 12; the `DataSet` JSON shape, video 11). |
| 11:30–14:00 | Model binding and validation | Binding source rules, table below. `FeeSchedulesController` checks `ModelState.IsValid` by hand and returns Web API 2's error shape. ASP.NET Core returns `ValidationProblemDetails`. The AngularJS client reads `response.data.Message`: with ProblemDetails it gets `undefined` and shows a generic error. Contract-test error responses too. |
| 14:00–16:30 | Filters, authorization and the user | The global `AuthorizeAttribute` becomes `RequireAuthorization()` on each group, or a fallback policy (then health endpoints need `AllowAnonymous`). `[Authorize(Roles = ...)]` still works; named policies are better (video 20). `HttpContext.Current.User.Identity.Name` becomes a `ClaimsPrincipal` parameter. MVC 5 filters map to MVC filters or endpoint filters. |
| 16:30–18:30 | What's left, and controllers vs minimal APIs | Walk the remaining controllers and what each drags along: Unity (video 08), `DataSet` and CSV (videos 11 and 12), lazy loading (video 13), PDF rendering (video 21). Sketch the billing group below. Decide controllers vs minimal APIs once per service, and write it down. |
| 18:30–20:00 | Recap | The mapping table, and the two things that break silently: error-response shape and pipeline behaviours that used to come from `Web.config`. |

### Mapping

| .NET Framework (`System.Web`) | ASP.NET Core |
|---|---|
| `Global.asax` `Application_Start` | `WebApplication.CreateBuilder` + `builder.Services` |
| `Application_EndRequest` cleanup | Scoped services disposed with the request scope |
| `Application_Error` | `AddProblemDetails()` + `UseExceptionHandler()` (optionally `IExceptionHandler`) |
| `IHttpModule` | Middleware (`app.Use...`), in explicit order |
| `IHttpHandler` | Endpoint (`app.MapGet`/`MapPost`...) or terminal middleware |
| `ApiController`, `[RoutePrefix]`, `[Route]` | `MapGroup` + minimal API endpoints, or `ControllerBase` + `[ApiController]` |
| `IHttpActionResult`, `Ok()`, `NotFound()` | `TypedResults.Ok()`, `TypedResults.NotFound()`, `Results<...>` |
| `HttpResponseMessage`, `Request.CreateResponse` | No equivalent; `IResult` (`TypedResults.File`, `TypedResults.Stream`, ...) |
| Global `AuthorizeAttribute` filter | `RequireAuthorization()` or a fallback authorization policy |
| MVC 5 action filters | MVC filters (controllers) or `IEndpointFilter` (minimal APIs) |
| `HttpContext.Current` | Explicit parameters; `IHttpContextAccessor` only where unavoidable |
| `<globalization culture="auto">` | `UseRequestLocalization` (opt-in) |
| `<sessionState>` | `AddSession`/`UseSession` (opt-in, different API) |

### Model binding differences

| Concern | Web API 2 (legacy) | ASP.NET Core |
|---|---|---|
| Binding sources | Simple types from the URI; one complex type from the body | Route and query for simple types, body for complex types (by convention with `[ApiController]` and in minimal APIs); DI services and `ClaimsPrincipal`, `CancellationToken` recognized automatically |
| Invalid model | `ModelState.IsValid` checked by hand; `BadRequest(ModelState)` → `{ "Message": ..., "ModelState": {...} }` | `[ApiController]`: automatic 400 `ValidationProblemDetails` → `{ "type", "title", "status", "errors" }`. Minimal APIs: validation is opt-in (.NET 10 adds built-in support; verify the API before recording) |
| Missing required parameter | Can fail action selection rather than binding: capture what legacy returns with a contract test | Minimal APIs return 400 |
| JSON | Newtonsoft, PascalCase output as configured here | `System.Text.Json`, camelCase output by default; property matching on input is case-insensitive under web defaults |

### Before and after

Legacy (`legacy/FeeBilling.Web/Controllers/Api/AccountsController.cs`):

```csharp
[HttpGet, Route("{id:int}")]
public IHttpActionResult GetAccount(int id)
{
    var db = DbContextFactory.Current;
    var account = db.Accounts.Find(id);
    if (account == null) return NotFound();
    return Ok(AccountDto.From(account));
}
```

Migrated (`src/FeeBilling.Accounts.Api/Accounts/AccountsEndpoints.cs`):

```csharp
private static async Task<Results<Ok<AccountDto>, NotFound>> GetAccount(
    int id,
    FeeBillingDbContext db,
    CancellationToken cancellationToken)
{
    var account = await db.Accounts
        .AsNoTracking()
        .Include(a => a.Household)
        .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    if (account is null)
    {
        return TypedResults.NotFound();
    }
```

Still on legacy (`BillingController`):

```csharp
[HttpPost, Route("runs")]
public IHttpActionResult StartRun(BillingRunRequest req)
{
    var user = HttpContext.Current.User.Identity.Name;
    var runId = BillingRunService.Enqueue(req.FirmId, req.PeriodEnd, user);
    return Ok(new { runId });   // double-click = two billing runs = double-billed clients
}
```

Sketch of the same endpoint in a future `Billing.Api` (idempotency comes in video 12):

```csharp
var billing = app.MapGroup("/api/billing")
    .RequireAuthorization()
    .WithTags("Billing");

billing.MapPost("/runs", async Task<Ok<StartRunResponse>> (
    BillingRunRequest request,
    ClaimsPrincipal user,
    IBillingRunService runs,
    CancellationToken cancellationToken) =>
{
    var runId = await runs.EnqueueAsync(request.FirmId, request.PeriodEnd, user.Identity?.Name, cancellationToken);
    return TypedResults.Ok(new StartRunResponse(runId));
});

// The AngularJS client reads response.runId: the property must serialize as "runId" (video 11).
public sealed record StartRunResponse([property: JsonPropertyName("runId")] int RunId);
```

A fallback policy instead of per-group `RequireAuthorization()`:

```csharp
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Endpoints with no authorization metadata now require a user, including health checks:
app.MapHealthChecks("/health").AllowAnonymous();
```

## Demo

```bash
# The pipeline, as migrated
dotnet run --project src/FeeBilling.Accounts.Api        # :5101

# Without the legacy app running, remote authentication fails for every request.
# UseExceptionHandler turns that into a ProblemDetails 500: show the body.
curl -i "http://localhost:5101/api/accounts?firmId=1"

# The integration tests swap remote auth for a test scheme and exercise the real pipeline
dotnet test tests/FeeBilling.Accounts.Api.Tests         # needs Docker (Testcontainers)

# What's left to port
git grep -nE "HttpContext\.Current|HttpResponseMessage|Request\.CreateResponse|ModelState" -- legacy/FeeBilling.Web/Controllers
```

## Traps to call out

- **Porting `HttpResponseMessage` literally.** It compiles, and the client gets a JSON serialization of the message object instead of the response you meant.
- **Assuming error responses don't matter.** The AngularJS app reads `response.data.Message`. ProblemDetails has `title` and `detail`. Contract-test errors, not just successes.
- **Losing `Web.config` behaviour without noticing.** `culture="auto"` changed parsing and formatting per request; nothing in ASP.NET Core does that unless you add it. Decide deliberately.
- **Middleware in the wrong order.** Authorization before authentication, or the exception handler added late, fails in ways that look like auth or serialization bugs.
- **A fallback policy that locks out health checks.** Orchestrators then restart healthy containers. Mark probes `AllowAnonymous()`.
- **Authentication that runs for every request.** In `Accounts.Api`, remote authentication calls legacy even for `/health`, so health checks fail when IIS is down (handover). Know which endpoints run which middleware.
- **Reaching for `IHttpContextAccessor` to preserve `HttpContext.Current` patterns.** It's the same ambient-state problem with a new name (video 08).
- **Mixing controllers and minimal APIs within one service without a reason.** Two conventions, two sets of filters, two OpenAPI stories.

## Key terms

Middleware pipeline · endpoint routing · minimal APIs · `MapGroup` · `TypedResults` / `Results<...>` · ProblemDetails (RFC 9457) · `[ApiController]` · binding source inference · endpoint filter · fallback authorization policy · `ClaimsPrincipal` binding

## After the video

1. Port `FeeSchedulesController.GetSchedules` and `GetSchedule` to minimal API endpoints in a new `Billing.Api`, including `.WithName("GetFeeSchedule")` so a create endpoint can return `TypedResults.CreatedAtRoute`.
2. Capture legacy's error response for an invalid fee schedule (`POST /api/feeschedules` with a missing `Code`) and decide whether the new API matches it or the AngularJS client changes first.
3. Write down, for your team, the rule for when an endpoint may use `IHttpContextAccessor`.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 9 (WP-03) and Section 13 (cheat sheet)
- Microsoft Learn: *Migrate from ASP.NET Framework to ASP.NET Core*, *ASP.NET Core middleware*, *Minimal APIs overview*, *Choose between controller-based APIs and minimal APIs*
- Microsoft Learn: *Handle errors in ASP.NET Core APIs* (ProblemDetails), *Model binding in ASP.NET Core*
- RFC 9457, *Problem Details for HTTP APIs*
