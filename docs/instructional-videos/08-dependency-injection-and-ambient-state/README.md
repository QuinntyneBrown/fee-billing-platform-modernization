# 08 · Dependency Injection and Ambient State

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work packages:** WP-02, WP-04 (and every WP that touches `FeeBilling.Core`) · **Prerequisites:** 06, 07

## Why this video exists

Legacy FeeBilling gets its most important dependency, the `DbContext`, from **ambient state**: `DbContextFactory.Current` reads `HttpContext.Current.Items`. That hidden dependency explains why the fee calculators are static, why the Windows Service has to fake an `HttpContext`, why the test suite has flaky ignored tests, and why one web request can hold two different `DbContext` instances. Interviewers use "how do you get rid of `HttpContext.Current`?" to separate people who have migrated real code from people who have read the cheat sheet. This video answers it with the FeeBilling code, then covers the DI rules (lifetimes, captive dependencies, scopes in background services) that the new code has to get right.

## Learning objectives

By the end, the viewer can:

- Explain why ambient state (`HttpContext.Current`, statics, `HttpRuntime.Cache`) blocks both testing and hosting outside IIS, using `DbContextFactory` and `FakeHttpContext` as the example.
- Map Unity 5 registrations and lifetime managers onto `Microsoft.Extensions.DependencyInjection`, and say what the built-in container doesn't do.
- Spot and explain a captive dependency, and show how `ValidateScopes` / `ValidateOnBuild` catch it at startup.
- Use a scoped `DbContext` correctly from a singleton or `BackgroundService` (`IServiceScopeFactory`), and from parallel code (`IDbContextFactory<T>`).
- Plan an incremental refactor from static classes to injected services without breaking legacy callers.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do you remove `HttpContext.Current` from a codebase? | Find what it's actually used *for* (here: a per-request `DbContext`, the user name, a cache). Pass data explicitly (user name as a parameter), inject services for behaviour (`DbContext`, `TimeProvider`, `ILogger<T>`). Use `IHttpContextAccessor` only at the edge, sparingly. SystemWebAdapters can keep shared code compiling during the transition, but it's a bridge, not the destination. |
| What's a captive dependency? | A longer-lived service holding a shorter-lived one, e.g. a singleton cache holding a scoped `DbContext`. The `DbContext` then outlives its request, is shared across threads, and tracks stale entities. The built-in container detects it when scope validation is on (on by default in Development). |
| How do you use a scoped `DbContext` from a background service? | `BackgroundService` is a singleton. Inject `IServiceScopeFactory`, create an async scope per unit of work, and resolve the context from it. For parallel work, use `IDbContextFactory<T>` and create one context per task. Never share a context across threads. |
| Unity/Autofac or the built-in container? | Built-in by default: it covers constructor injection, the three lifetimes, open generics, `IEnumerable<T>` and keyed services (.NET 8+). Keep Autofac if you truly need property injection, interception, child containers or convention scanning. Don't carry a container over just because it was there. |
| Why were the legacy fee calculators static, and why does that matter? | Static classes can't receive constructor dependencies, so they reached for ambient state instead. That made them untestable without a database and an `HttpContext`, and impossible to call safely from other threads. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Data/DbContextFactory.cs` | `HttpContext.Current.Items[ItemsKey]`: the ambient `DbContext` |
| `legacy/FeeBilling.Web/App_Start/UnityConfig.cs` | `HierarchicalLifetimeManager` registration, and the comment that injected and ambient contexts are *not the same instance* |
| `legacy/FeeBilling.Web/Controllers/Api/FeeSchedulesController.cs` | Constructor-injected `FeeBillingEntities` ("Not the same context as DbContextFactory.Current") |
| `legacy/FeeBilling.Web/Global.asax.cs` | `Application_EndRequest` → `DbContextFactory.DisposeCurrent()` |
| `legacy/FeeBilling.BillingRunner/FakeHttpContext.cs` | Faking a request so the calculators work outside IIS |
| `legacy/FeeBilling.BillingRunner/BillingRunnerService.cs` | `HttpContext.Current = _fakeContext;` on timer threads and inside `Parallel.ForEach` (FB-402) |
| `legacy/FeeBilling.Core/FeeCalculator.cs` | Static class, static `ILog`, `HttpRuntime.Cache` |
| `legacy/FeeBilling.Tests/Core/FeeCalculatorTests.cs` | `[Ignore("Flaky - cache from previous test")]`: static state leaking between tests |
| `src/FeeBilling.Infrastructure/DependencyInjection.cs` | The modern registration: `AddDbContext<FeeBillingDbContext>` |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Open `UnityConfig.cs`. One request, two `DbContext`s: the one Unity injected into `FeeSchedulesController`, and the one `DbContextFactory.Current` hands to `FeeCalculator`. Ask: which one saw your write? |
| 01:30–05:00 | Anatomy of ambient state | `DbContextFactory.Current` → `HttpContext.Current.Items`. It "works" in IIS because ASP.NET flows `HttpContext` with the request. Outside IIS it's `null`: the BillingRunner fakes one, re-assigns it on timer threads, and again on thread-pool threads (FB-402). Static `HttpRuntime.Cache` makes tests order-dependent (the ignored flaky test). |
| 05:00–08:30 | Unity → Microsoft.Extensions.DependencyInjection | Map the lifetimes (table below). Unity's `RegisterType` without a lifetime manager is transient. The MVC and Web API resolvers (`DependencyResolver.SetResolver`, `UnityHierarchicalDependencyResolver`) disappear: ASP.NET Core has one container. What the built-in container lacks, and when Autofac is still justified. |
| 08:30–12:00 | Lifetimes and captive dependencies | Singleton / scoped / transient with FeeBilling examples: `FeeEngine` (stateless, singleton), `FeeBillingDbContext` (scoped), a validator (transient). Live demo: a singleton `ScheduleCache` that takes the `DbContext` fails at `Build()` in Development. Explain why the failure only happens at startup when `ValidateOnBuild` is on, and silently otherwise. |
| 12:00–15:00 | Scopes outside a request | `BackgroundService` is a singleton, so it needs `IServiceScopeFactory.CreateAsyncScope()` per unit of work. For the billing worker's parallel chunks, `IDbContextFactory<T>`: one context per chunk. Contrast with the legacy `Parallel.ForEach` over one shared `_db` (the full story is in video 15). |
| 15:00–17:30 | Refactoring path | Step 1: turn a static into an instance class with constructor dependencies, and keep a thin static facade for legacy callers. Step 2: pass data explicitly (the controller passes `User.Identity.Name`, so `BillingRunService.Enqueue` never reads `HttpContext`). Step 3: delete the facade once the last caller has moved. Bridge option: `Microsoft.AspNetCore.SystemWebAdapters` lets a `netstandard2.0` library compile against `System.Web.HttpContext`, so shared code runs in both hosts. Use it to buy time, and don't let it become the design. |
| 17:30–19:00 | Choosing implementations | Strategy selection: `IEnumerable<IFeeStrategy>` with a `Handles` property (the WP-02 design), or keyed services (.NET 8+) keyed by `FeeScheduleType`. Static `ILog` → injected `ILogger<T>` (structured logging is in video 19). `DateTime.Now` → injected `TimeProvider`. |
| 19:00–20:00 | Recap | Ambient state is a hidden parameter. Make it an explicit parameter or an injected dependency, pick the right lifetime, and let the container's validation prove it at startup. |

### Unity to Microsoft.Extensions.DependencyInjection

| Unity 5 | Built-in container | FeeBilling example |
|---|---|---|
| `RegisterType<T>()` (no lifetime manager) | `AddTransient<T>()` | `IInvoicePdfRenderer` in `UnityConfig` |
| `HierarchicalLifetimeManager` (per child container = per request) | `AddScoped<T>()` | `FeeBillingEntities` → `AddDbContext<FeeBillingDbContext>()` |
| `ContainerControlledLifetimeManager` | `AddSingleton<T>()` | A stateless `FeeEngine` |
| `InjectionConstructor()` (pick a constructor) | Factory overload: `AddScoped(sp => new T(...))` | Rarely needed; prefer one public constructor |
| Named registrations | Keyed services: `AddKeyedScoped<T>(key)` + `[FromKeyedServices(key)]` | One `IFeeStrategy` per `FeeScheduleType` |

### Before: ambient `DbContext`

```csharp
// legacy/FeeBilling.Data/DbContextFactory.cs
public static FeeBillingEntities Current
{
    get
    {
        var items = HttpContext.Current.Items;
        var db = items[ItemsKey] as FeeBillingEntities;
        if (db == null)
        {
            db = new FeeBillingEntities();
            items[ItemsKey] = db;
        }
        return db;
    }
}
```

```csharp
// legacy/FeeBilling.BillingRunner/BillingRunnerService.cs
Parallel.ForEach(accounts.Where(a => !BillingRunService.IsHouseholdBilled(a)), acct =>   // shared DbContext across threads
{
    HttpContext.Current = _fakeContext;   // FB-402: pool threads have no HttpContext either
```

### After: explicit dependencies (sketch)

```csharp
// Billing.Api / Billing.Worker composition root
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FeeEngine>();                       // pure and stateless
builder.Services.AddDbContext<FeeBillingDbContext>(o => o.UseSqlServer(connectionString));   // scoped
```

```csharp
// A singleton background service using scoped services correctly
public sealed class BillingRunPoller(IServiceScopeFactory scopes, ILogger<BillingRunPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = scopes.CreateAsyncScope();          // one scope per unit of work
            var db = scope.ServiceProvider.GetRequiredService<FeeBillingDbContext>();
            // ... load the next pending run, process it
            logger.LogDebug("Polled billing run queue");
        }
    }
}
```

```csharp
// Parallel work: one context per task, never a shared one
builder.Services.AddDbContextFactory<FeeBillingDbContext>(o => o.UseSqlServer(connectionString));

await Parallel.ForEachAsync(chunks, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
    async (chunk, token) =>
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        // load inputs, run FeeEngine, write invoices for this chunk
    });
```

## Demo

```bash
# How far does the ambient context reach?
git grep -c "DbContextFactory.Current" -- legacy
git grep -n "HttpContext.Current" -- legacy

# The static cache that makes tests order-dependent
git grep -n "HttpRuntime.Cache" -- legacy
git grep -n "Flaky" -- legacy/FeeBilling.Tests
```

Captive dependency, live, in a throwaway web project **outside** the repo (so the repo's central package management doesn't apply):

```csharp
// Program.cs
builder.Services.AddDbContext<AppDb>(o => o.UseSqlServer(connectionString));
builder.Services.AddSingleton<ScheduleCache>();      // ScheduleCache(AppDb db): singleton holding a scoped service

var app = builder.Build();                            // Development: throws here
```

Run it with `ASPNETCORE_ENVIRONMENT=Development` and read the error aloud: *Cannot consume scoped service 'AppDb' from singleton 'ScheduleCache'.* Then run with `ASPNETCORE_ENVIRONMENT=Production`: it starts, and the bug ships. Point out that scope validation is a Development default, so CI integration tests should run in Development or enable validation explicitly.

## Traps to call out

- **Replacing `HttpContext.Current` with `IHttpContextAccessor` everywhere.** That's the same ambient dependency with a new name, and it still breaks in the worker. Pass the data you need.
- **Two contexts per request.** Legacy mixes the injected `FeeBillingEntities` with `DbContextFactory.Current`. Entities loaded by one aren't tracked by the other, and `SaveChanges` on one doesn't save the other. In the new code, one scope means one context.
- **Resolving scoped services from the root provider.** `app.Services.GetRequiredService<FeeBillingDbContext>()` outside a scope creates a context that lives for the whole app. Scope validation catches it in Development.
- **Turning off validation to "fix" the startup error.** The error is the container telling you about a real threading and staleness bug.
- **Sharing one `DbContext` across `Parallel.ForEach` / `Task.WhenAll`.** EF Core throws on concurrent use; EF6 often didn't, which is why the legacy runner "worked".
- **Static loggers and static clocks.** `LogManager.GetLogger` and `DateTime.Now` inside the calculators are hidden dependencies too. `ILogger<T>` and `TimeProvider` make them visible and testable.
- **Letting the SystemWebAdapters bridge become permanent.** It keeps `HttpContext.Current` compiling in shared code; it doesn't remove the coupling.

## Key terms

Ambient context · composition root · service lifetime (singleton, scoped, transient) · captive dependency · scope validation (`ValidateScopes`, `ValidateOnBuild`) · `IServiceScopeFactory` · `IDbContextFactory<T>` · keyed services · `TimeProvider` · seam

## After the video

1. List every caller of `DbContextFactory.Current` in `legacy/` and classify what each one needs: a query, the user name, or nothing (dead code).
2. Sketch the constructor of an instance-based replacement for `BillingRunService.CalculateAccountFee` that has no `HttpContext`, no static `ILog` and no `DateTime.Now`.
3. Write down the registration (lifetime and reason) for each service the future `FeeBilling.Billing.Worker` will need.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 6.3 (code smells), WP-02, WP-04, Section 13 (cheat sheet)
- `legacy/README.md`: "Calling the legacy fee engine from your own code"
- Microsoft Learn: *Dependency injection in .NET*, *Service lifetimes*, *Use scoped services within a BackgroundService*
- Microsoft Learn: *DbContext lifetime, configuration, and initialization* (`AddDbContextFactory`)
- Microsoft Learn: *ASP.NET Core incremental migration with System.Web adapters* (verify the supported `HttpContext` members before recording)
