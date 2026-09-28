# 08 · Dependency Injection and Ambient State

Welcome. This lesson is about hidden dependencies, and how to get rid of them. Legacy FeeBilling gets its most important dependency, the database context, from ambient state: a static property that reaches into the current HTTP request. That one design choice explains why the fee calculators are static, why the Windows Service has to fake a web request, why tests are flaky, and why a single request can hold two different database contexts. By the end of this lesson, you'll be able to explain how to remove `HttpContext.Current` from a codebase, how service lifetimes work in the built-in container, what a captive dependency is, and how to use a scoped database context safely from background and parallel code.

## The questions this lesson answers

Here are the questions this lesson prepares you for. How do you remove `HttpContext.Current` from a codebase? What's a captive dependency? How do you use a scoped `DbContext` from a background service? Would you keep Unity or Autofac, or move to the built-in container? And a question about the legacy code itself: why were the fee calculators static, and why does that matter?

Interviewers like the first question because it separates people who've migrated real code from people who've read a cheat sheet. The cheat-sheet answer is "use `IHttpContextAccessor`." The real answer starts with a question of your own: what is the code actually using `HttpContext` for?

## What ambient state is, and why it hurts

Let's define the term. Ambient state is data or a service that code reaches out and grabs from somewhere global, instead of receiving it as a parameter or through its constructor. `HttpContext.Current` is the classic example in .NET Framework. So is a static cache like `HttpRuntime.Cache`, a static logger, and `DateTime.Now`, which is really a dependency on the system clock.

The problem with ambient state is that it's a hidden parameter. The method signature says "give me an account ID and a period end," but the method secretly also needs a live web request, a database connection hanging off that request, a warm cache, and the current time. None of that shows up in the signature, so callers can't provide it, tests can't control it, and other hosts can't supply it.

Ambient state also tends to be tied to a thread. In classic ASP.NET, the framework flows `HttpContext.Current` along with the request. On a timer thread, or a thread-pool thread started by `Parallel.ForEach`, it's simply null. So code that "works" in IIS breaks the moment you call it from anywhere else.

Dependency injection is the fix. A class declares what it needs in its constructor. Something at the edge of the application, called the composition root, decides how to build those things and how long they live. The dependency stops being hidden, and it becomes replaceable.

## Ambient state in FeeBilling

Now let's look at the real thing. Open `legacy/FeeBilling.Data/DbContextFactory.cs`. It has a static property called `Current`. When you read it, it looks in `HttpContext.Current.Items` for a context stored under a fixed key. If there isn't one, it creates a new `FeeBillingEntities`, stashes it in the items collection, and returns it. So there's one Entity Framework context per HTTP request, stored on the request itself. `Global.asax.cs` cleans up at the end of each request: `Application_EndRequest` calls `DbContextFactory.DisposeCurrent()`.

Who uses it? A quick `git grep` shows `DbContextFactory.Current` in every Core class that touches data: `FeeCalculator`, `BlendedFeeCalculator`, `FlatFeeCalculator`, `HouseholdFeeService`, `AumService` and `BillingRunService`, plus several of the Web API controllers. And every one of those Core classes is static. That's not a coincidence. A static class can't receive constructor dependencies, so it has to reach for ambient state. The static design and the ambient context reinforce each other.

Now look at what happens outside IIS. `legacy/FeeBilling.BillingRunner/FakeHttpContext.cs` builds a fake `HttpRequest` for localhost, a fake response writing to a string writer, and a generic principal called BillingRunner. The comment explains why: the calculators get their context from `HttpContext.Current.Items`, and outside IIS there is no HTTP context, so the service makes one up.

Then open `BillingRunnerService.cs`. In `OnStart`, it assigns the fake context to `HttpContext.Current`. But the work happens on a timer, and the comment in `ProcessQueue` says timer threads don't inherit `HttpContext`, so it assigns it again. Then it calls `Parallel.ForEach` over the firm's accounts, and inside the loop body there's a third assignment, with the comment: FB-402, pool threads have no `HttpContext` either. Every one of those lines is a patch over the same root cause. And look at what the parallel loop shares: every thread uses the one fake context, which means every thread uses the one `FeeBillingEntities` stored in it. A single database context, used concurrently from many threads. We'll come back to that.

Static state also leaks into tests. `FeeCalculator` caches billable assets under management in `HttpRuntime.Cache`, keyed by account and date, for thirty minutes of local time. In `legacy/FeeBilling.Tests/Core/FeeCalculatorTests.cs`, one test is marked `Ignore` with the reason: Flaky, cache from previous test. That's what static caches do: the result of one test depends on which tests ran before it.

Finally, the strangest one. Open `legacy/FeeBilling.Web/App_Start/UnityConfig.cs`. Unity registers `FeeBillingEntities` with a `HierarchicalLifetimeManager`, which in the Web API integration means one instance per request. And the comment right above it says: newer controllers take `FeeBillingEntities` in the constructor; older code uses `DbContextFactory.Current`; they are not the same instance within a request. `FeeSchedulesController` confirms it: its constructor takes an injected context, with the comment "Not the same context as DbContextFactory.Current." So if a controller saves a change through its injected context, and then calls a Core service that reads through the ambient context, the second context never saw the first context's tracked entities. One request, two units of work, and nobody designed it that way.

## From Unity to the built-in container

In ASP.NET Core, and in any .NET 10 host, there's one built-in container, `Microsoft.Extensions.DependencyInjection`. The legacy app needed two adapters to plug Unity into two frameworks: `DependencyResolver.SetResolver` for MVC and `UnityHierarchicalDependencyResolver` for Web API. Both disappear. The host builds one service provider, and everything resolves from it.

Mapping the Unity registrations is mostly mechanical. A Unity `RegisterType` with no lifetime manager creates a new instance every time, which is transient. So the `IInvoicePdfRenderer` registration becomes `AddTransient`. The hierarchical lifetime manager, one instance per child container, which here means per request, becomes scoped. For a database context you don't call `AddScoped` yourself; you call `AddDbContext`, which registers it as scoped. That's exactly what `src/FeeBilling.Infrastructure/DependencyInjection.cs` already does for the new code: `AddDbContext<FeeBillingDbContext>` with SQL Server. Unity's container-controlled lifetime manager is a singleton, `AddSingleton`. Unity's `InjectionConstructor`, which picks a specific constructor, becomes a factory overload, though the better fix is to give the class one public constructor. And Unity's named registrations map onto keyed services, which arrived in .NET 8: `AddKeyedScoped` to register, and the `FromKeyedServices` attribute to consume.

What doesn't the built-in container do? No property injection, no interception or decorators out of the box, no child containers, and no convention-based assembly scanning. If you genuinely need those, Autofac still plugs into the .NET host. But don't carry a third-party container forward just because the legacy app had one. Constructor injection, three lifetimes, open generics, `IEnumerable` of a service for multiple implementations, and keyed services cover almost everything a service like Billing.Api needs.

## Lifetimes, captive dependencies and scopes outside a request

Three lifetimes, with a FeeBilling example for each. A singleton lives for the whole application. The future `FeeEngine` is pure and stateless, so a singleton is right. Scoped means one instance per scope, and in a web app a scope is a request. `FeeBillingDbContext` is scoped. Transient means a new instance every time it's resolved: a small validator, for example.

The rule that follows is simple: a service can depend on things that live as long as it does or longer, never shorter. Break that rule and you get a captive dependency. Picture a singleton `ScheduleCache` whose constructor takes the database context. The first request creates the cache, the cache captures that request's context, and the context now lives forever. It outlives its request, it's shared across every thread that touches the cache, and it keeps tracking entities that are long stale.

The built-in container can catch this, but only if you let it. Scope validation, the `ValidateScopes` and `ValidateOnBuild` options, is on by default in the Development environment. With it on, building the app throws: cannot consume scoped service from singleton. In Production it's off by default, so the app starts and the bug ships. Two consequences: run your integration tests in Development, or turn validation on explicitly; and never switch validation off to make the startup error go away. The error is the container telling you about a real threading bug.

Now, scopes outside a request. A `BackgroundService` is registered as a hosted service, which is effectively a singleton. So it must not take `FeeBillingDbContext` in its constructor, or it becomes exactly the captive dependency we just described. Instead, it takes `IServiceScopeFactory`. For each unit of work, say each poll of the billing run queue, it calls `CreateAsyncScope`, resolves the context from that scope's service provider, does the work, and disposes the scope. One scope, one context, one unit of work.

Parallel work needs one more tool. A database context is not thread-safe, in EF6 or EF Core. The difference is that EF Core detects concurrent use and throws an `InvalidOperationException`, while EF6 often didn't notice, which is why the legacy `Parallel.ForEach` over a shared context "worked." For the future billing worker, register `AddDbContextFactory`, inject `IDbContextFactory<FeeBillingDbContext>`, and inside each parallel task call `CreateDbContextAsync` to get a context that belongs to that task alone. Combine that with `Parallel.ForEachAsync` and a bounded `MaxDegreeOfParallelism`, and each chunk of accounts gets its own context. Lesson fifteen covers the worker in full.

## A refactoring path that doesn't break legacy

You rarely get to rewrite every caller at once, so here's an incremental path.

Step one: find out what `HttpContext` is actually used for. In FeeBilling it's three things: a per-request database context, the current user's name, and a cache. Each has a different fix.

Step two: pass data explicitly. `BillingController.StartRun` reads `HttpContext.Current.User.Identity.Name` and passes it to `BillingRunService.Enqueue`. Good: the service already takes the user name as a parameter. That's the pattern. The controller, which lives at the edge, reads the user; the service never touches `HttpContext`. Push that pattern everywhere.

Step three: turn a static class into an instance class with constructor dependencies, the database context, a `TimeProvider` instead of `DateTime.Now`, an `ILogger<T>` instead of the static log4net logger. Keep a thin static facade with the old method names, so legacy callers keep compiling, and have the facade build the instance. Then move callers across one at a time, and delete the facade when the last one is gone.

For choosing implementations, the work package design uses `IEnumerable<IFeeStrategy>`, where each strategy exposes a `Handles` property, and the engine picks the one that handles the schedule type. Keyed services, keyed by fee schedule type, are the alternative. Either replaces the switch statement in `BillingRunService.CalculateAccountFee`.

There's also a bridge worth knowing about. The `Microsoft.AspNetCore.SystemWebAdapters` package lets a `netstandard2.0` library compile against `System.Web.HttpContext`, so shared code can keep using `HttpContext.Current` in both the Framework host and an ASP.NET Core host during the transition. Check which members it supports before you rely on it. Use it to buy time, and don't let it become the design: it keeps the coupling, it just makes it portable.

## Traps

A few traps to call out.

Replacing `HttpContext.Current` with `IHttpContextAccessor` everywhere. That's the same ambient dependency with a new name, and it's still null in a worker. Use the accessor sparingly, at the edge, and pass the data you need.

Two contexts per request. Legacy mixes an injected context with the ambient one. In the new code, one scope means one context.

Resolving scoped services from the root provider, for example asking `app.Services` for the database context outside any scope. That creates a context that lives as long as the app. Scope validation catches it in Development.

Turning validation off to fix a startup error. Fix the lifetime instead.

Sharing one context across `Parallel.ForEach` or `Task.WhenAll`. EF Core will throw, and that's good news: the bug was always there.

Static loggers and static clocks. `LogManager.GetLogger` and `DateTime.Now` inside the calculators are hidden dependencies too.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do you remove `HttpContext.Current` from a codebase?

[pause 5s]

First I find out what it's used for, because it's usually several things. In FeeBilling it's a per-request database context, the user name, and a cache. For data like the user name, I pass it explicitly: the controller reads it at the edge and passes it as a parameter. For behaviour, like the database context, a clock or a logger, I inject a service through the constructor, registered with the right lifetime. I use `IHttpContextAccessor` only at the edge and sparingly. During the transition, SystemWebAdapters can keep shared code compiling in both hosts, but that's a bridge, not the destination.

**Interviewer:** What's a captive dependency?

[pause 5s]

A longer-lived service holding a shorter-lived one, for example a singleton cache that takes a scoped database context in its constructor. The context then outlives its request, gets shared across threads, and tracks stale entities. The built-in container detects it at startup when scope validation is on, which is the default in Development, so I run integration tests in Development or enable validation explicitly, and I never disable it to make the error go away.

**Interviewer:** How do you use a scoped `DbContext` from a background service?

[pause 5s]

A `BackgroundService` is effectively a singleton, so I inject `IServiceScopeFactory`, create an async scope per unit of work, resolve the context from that scope, and dispose it when the unit of work is done. For parallel work, I use `IDbContextFactory` and create one context per task. I never share a context across threads. The legacy billing runner did, with `Parallel.ForEach` over one context; EF6 often didn't notice, but EF Core throws.

**Interviewer:** Would you keep Unity or Autofac, or use the built-in container?

[pause 5s]

The built-in container by default. It covers constructor injection, the three lifetimes, open generics, multiple implementations through `IEnumerable`, and keyed services since .NET 8. I'd keep Autofac only if I genuinely needed property injection, interception, child containers or convention scanning. The mapping from Unity is mostly mechanical: no lifetime manager is transient, hierarchical is scoped, container-controlled is singleton.

**Interviewer:** Why were the legacy fee calculators static, and why does that matter?

[pause 5s]

Because a static class can't receive constructor dependencies, the calculators reached for ambient state: `DbContextFactory.Current`, which reads `HttpContext.Current.Items`, plus a static cache and a static logger. That made them untestable without a database and a web request, forced the Windows Service to fake an `HttpContext` on every thread, and made them unsafe to call in parallel. Turning them into instance classes with explicit dependencies is what makes them testable and hostable anywhere.

## Recap

Five things to remember from this lesson.

One: ambient state is a hidden parameter. `HttpContext.Current`, static caches, static loggers and `DateTime.Now` all hide what a method really needs.

Two: in FeeBilling, `DbContextFactory.Current` reads the context from `HttpContext.Current.Items`, which is why the calculators are static, why the billing runner fakes an `HttpContext` three times, and why one request can hold two contexts.

Three: map Unity onto the built-in container by lifetime: transient, scoped, singleton, and keyed services for named registrations. Keep Autofac only for features you actually use.

Four: a service may depend only on things that live at least as long as it does. Let scope validation prove it at startup.

Five: outside a request, create a scope per unit of work with `IServiceScopeFactory`, and in parallel code give every task its own context from `IDbContextFactory`.

In the next lesson, we'll look at the gateway itself: how YARP routes one path at a time, and how you roll a route back.
