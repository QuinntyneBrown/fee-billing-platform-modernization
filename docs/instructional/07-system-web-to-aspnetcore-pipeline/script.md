# 07 · From System.Web to the ASP.NET Core Pipeline

Welcome back. `System.Web` doesn't exist in modern .NET. So every part of the legacy web application that depends on it, the `Global.asax` file, HTTP modules, Web API 2 controllers, `HttpResponseMessage`, and `HttpContext.Current`, has to be re-expressed in the ASP.NET Core pipeline. Interviewers test whether you know the mapping, and, more importantly, the behaviours that change along the way. FeeBilling has already done this once: the new Accounts API replaced two Web API 2 controllers with minimal API endpoints. In this lesson, we'll use that as the reference, and then map the three controllers still waiting to move.

## The questions this lesson answers

Here's what you'll be able to answer. What replaces `Global.asax`, HTTP modules and HTTP handlers? Minimal APIs or controllers? What happens to `HttpResponseMessage` and `IHttpActionResult`? How does model binding differ? And how do you get the current user without `HttpContext.Current`?

The two things to remember for this lesson are the ones that break silently: the shape of error responses, and behaviours that used to come for free from `Web.config`.

## From Global.asax to Program.cs

Start with application startup. Open `legacy/FeeBilling.Web/Global.asax.cs`. It has three event handlers that matter.

`Application_Start` configures log4net, registers areas, calls the Web API configuration, registers global filters and MVC routes, sets up the Unity container, and, since the migration began, configures the System Web Adapters remote app server. In ASP.NET Core, all of that registration becomes calls on the builder's service collection in `Program.cs`, before the app is built.

`Application_EndRequest` calls `DbContextFactory.DisposeCurrent`. That disposes the per-request database context that the legacy code stashed in `HttpContext.Current.Items`. In ASP.NET Core, this handler simply disappears. You register the database context as a scoped service, and the dependency injection container disposes it when the request's scope ends.

`Application_Error` logs any unhandled exception with log4net, with a message built by concatenating the request URL. In ASP.NET Core, the equivalent is two lines: `AddProblemDetails` on the services, and `UseExceptionHandler` in the pipeline. Together, they log the exception and return a standard error response in the Problem Details format, defined by RFC 9457.

Now open `src/FeeBilling.Accounts.Api/Program.cs` and you can see the new version. The builder adds the service defaults, the infrastructure, problem details, the JSON options, System Web Adapters, and authorization. Then the pipeline: `UseExceptionHandler` first, then `UseAuthentication`, then `UseAuthorization`, then `UseSystemWebAdapters`, and then the endpoints are mapped.

## Modules and handlers become middleware

In classic ASP.NET on IIS, the integrated pipeline raised a fixed sequence of events for every request: begin request, authenticate, authorize, and so on. HTTP modules subscribed to those events. HTTP handlers were the things that actually produced a response for a given path.

ASP.NET Core has no fixed event sequence. It has a middleware pipeline, and middleware runs in exactly the order you write it in `Program.cs`. Modules become middleware. Handlers become endpoints, or terminal middleware.

That shift has a consequence: order bugs are now your bugs. Authentication has to run before authorization. The exception handler has to be early, so it wraps everything after it. If you get the order wrong, the failures look like authentication or serialization bugs, not like ordering bugs, which makes them slow to diagnose.

FeeBilling's `Web.config` even shows a module being registered: under `system.webServer`, there's a modules section that adds `SystemWebAdapterModule`. That's the Framework half of the bridge the previous team added.

Now the silent part. Two behaviours in the legacy `Web.config` don't have to be written in code, so they're easy to lose without noticing.

First, the globalization element sets culture to auto, with a comment saying thread culture follows the browser's Accept-Language header. That means every request in legacy parses and formats numbers and dates using the user's browser culture. ASP.NET Core does nothing like that unless you add request localization middleware. So if you port an endpoint and don't think about it, number and date formatting quietly changes. Lesson eighteen goes deep on culture.

Second, the session state element sets up in-process session. ASP.NET Core has no session unless you add it, and even then it's a different API, and it isn't shared with the legacy app. Lesson ten covers sharing state between the two apps.

## Web API 2 becomes minimal APIs

Now the endpoints themselves. Put two files side by side.

`legacy/FeeBilling.Web/Controllers/Api/AccountsController.cs` is a Web API 2 `ApiController` with a route prefix for accounts. Its `GetAccount` action is attribute-routed on an integer ID, gets the context from `DbContextFactory.Current`, calls `Find`, returns `NotFound` if there's nothing there, and otherwise returns `Ok` with a DTO. The return type is `IHttpActionResult`.

`src/FeeBilling.Accounts.Api/Accounts/AccountsEndpoints.cs` is its replacement. It calls `MapGroup` for the accounts path, then `RequireAuthorization`, then `WithTags`. The `GetAccount` handler is a static async method. Its parameters are the ID, the `FeeBillingDbContext` injected by the container, and a cancellation token. It queries with `AsNoTracking` and an explicit include of the household, and it returns `TypedResults.NotFound` or `TypedResults.Ok`.

Look at its return type: a task of `Results` of `Ok` of `AccountDto`, and `NotFound`. That union type is the important part. It tells the compiler, the OpenAPI generator and your tests exactly which responses the endpoint can produce. With `IHttpActionResult`, all of that was invisible until runtime.

Route names carry over too, and they matter for create endpoints. `FeeSchedulesController` names its get-by-ID route `GetFeeSchedule`, and its create action returns `CreatedAtRoute` with that name, which produces a 201 response with a Location header pointing at the new schedule. In a minimal API, you give the get endpoint a name with `WithName`, and the create endpoint returns `TypedResults.CreatedAtRoute` with the same name. Miss the name, and the create endpoint fails at runtime, not at compile time.

Now look at the start-run action in `BillingController`. It returns `Ok` with an anonymous object containing `runId`. Because the C# property name itself is camelCase, the JSON property is camelCase too, even though the rest of the legacy API is PascalCase. The AngularJS client reads `response.runId`. So when you port it, give the response a small record type and pin the JSON name explicitly with a `JsonPropertyName` attribute. Otherwise a naming-policy change somewhere else in the service quietly breaks the one screen that starts billing runs.

So the mapping is: route prefix becomes `MapGroup`. `IHttpActionResult`, with its `Ok` and `NotFound` helpers, becomes `TypedResults` and the `Results` union. MVC 5 action filters become MVC filters if you use controllers, or endpoint filters if you use minimal APIs.

And `HttpResponseMessage`? There's no equivalent. Web API 2 let you build and return a raw response message, and `Request.CreateResponse` built one for you. ASP.NET Core 1 and 2 had a compatibility shim for this, but it was removed in ASP.NET Core 3.0. If you port a method that returns an `HttpResponseMessage`, it will compile, but ASP.NET Core treats it as an ordinary object and serializes it. The client gets JSON describing a message, not the response you meant.

FeeBilling has exactly that case waiting. Open `legacy/FeeBilling.Web/Controllers/Api/BillingController.cs`. The `GetInvoices` action returns an `HttpResponseMessage`. For CSV, it builds a string content response with an attachment header. Otherwise, it calls `Request.CreateResponse` with an OK status and a `DataSet`. Neither ports literally. The CSV becomes a file or stream result, and the `DataSet` JSON shape needs a deliberate decision. Lessons eleven and twelve cover both.

## Model binding and validation

Model binding is where porting an endpoint can break an existing client, even when the happy path works.

Web API 2 had a simple rule. Simple types, like integers and strings, bind from the URI. One complex type binds from the request body. ASP.NET Core infers binding sources by convention: route values and the query string for simple types, the body for complex types, and it also recognizes services from the container, the `ClaimsPrincipal`, and the cancellation token automatically.

The bigger difference is validation errors. Open `legacy/FeeBilling.Web/Controllers/Api/FeeSchedulesController.cs`. Its create and update actions check `ModelState.IsValid` by hand, and return `BadRequest` with the model state. In Web API 2, that produces a JSON object with a `Message` property and a `ModelState` property. In ASP.NET Core, a controller marked with the API controller attribute returns an automatic 400 response in the validation problem details format, with properties called type, title, status and errors. For minimal APIs, validation is opt-in; newer versions add built-in support, so check the current API before you rely on it.

Why does that matter? Open `legacy/FeeBilling.Web/App/billing/billing-review.controller.js`. When starting a billing run fails, it sets the error message to `response.data.Message`, falling back to a generic string. Against a Problem Details response, `Message` is undefined, so the user sees the generic error and loses the real reason. Nothing throws, nothing logs. It just gets worse.

So contract-test your error responses, not only your successes. Capture what legacy returns for an invalid request, and decide deliberately: either the new API matches the old error shape for the endpoints the old front end still calls, or the front end changes first.

JSON is another difference: Web API 2 used Newtonsoft, configured here to output PascalCase, while ASP.NET Core uses `System.Text.Json` with camelCase by default. That's lesson eleven.

## Filters, authorization and the current user

Open `legacy/FeeBilling.Web/App_Start/WebApiConfig.cs`. At the bottom, it adds a global `AuthorizeAttribute` filter, with a comment saying everything under the API path requires a Forms-auth cookie.

There are two ways to express that in ASP.NET Core. The first is what Accounts.Api does: call `RequireAuthorization` on each endpoint group. The second is a fallback authorization policy: any endpoint with no authorization metadata requires an authenticated user. The fallback is safer, because a new endpoint can't be accidentally anonymous. But it has a trap: health check endpoints then require a user too, and a container orchestrator that can't authenticate will think a healthy service is dead and restart it. So mark health endpoints with `AllowAnonymous`.

Role checks, like `FeeSchedulesController`'s authorize attribute requiring the ScheduleEditor role, still work in ASP.NET Core. Named policies are better, and lesson twenty covers them.

Then the current user. Legacy reads `HttpContext.Current.User.Identity.Name` inside controller actions, and in `BillingController.StartRun`, it passes that name into the static billing run service. In ASP.NET Core, you pass it in explicitly. Minimal API handlers can take a `ClaimsPrincipal` parameter, and controllers have a `User` property. `IHttpContextAccessor` exists, but it's the same ambient-state pattern with a new name. Keep it rare, and never use it in domain code. Lesson eight goes deeper.

And one more authentication detail from this repo. In Accounts.Api, remote authentication calls back into the legacy app on every request, and the handover notes that when IIS is down, Accounts.Api returns 500 on everything, including its health endpoint. Know which endpoints run which middleware.

## What's left, and controllers versus minimal APIs

Three legacy API controllers are still waiting: fee schedules, billing and invoices. Each one drags something along. The fee schedules controller uses a Unity-injected context and hand-rolled model state checks. The billing controller uses `HttpContext.Current`, `HttpResponseMessage` and a `DataSet`. The invoices controller relies on lazy loading, which EF Core doesn't do by default, and renders PDFs with `System.Drawing`. Those are lessons eight, eleven, twelve, thirteen and twenty-one.

Finally, minimal APIs or controllers? Both are fully supported, so this is a judgment call. Minimal APIs have less ceremony, `TypedResults` give accurate OpenAPI metadata, and they suit small vertical slices. Controllers are familiar to a team coming from Web API 2, with rich conventions and filters. The important thing is to pick one per service, write the decision down, and be consistent. Mixing both in one service without a reason gives you two conventions, two filter models and two OpenAPI stories. FeeBilling's first slice chose minimal APIs, so the next service has a reference to follow.

## Traps

Here are the traps.

Porting `HttpResponseMessage` literally. It compiles, and the client gets a serialized message object.

Assuming error responses don't matter. The legacy front end reads a `Message` property.

Losing `Web.config` behaviour without noticing, like per-request culture from the browser.

Middleware in the wrong order.

A fallback policy that locks out health checks.

Authentication that runs for every request, including health checks.

Reaching for `IHttpContextAccessor` to preserve old patterns.

And mixing controllers and minimal APIs in one service without a reason.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** What replaces `Global.asax`, HTTP modules and HTTP handlers?

[pause 5s]

`Program.cs`. Service registration happens on the builder, then an explicitly ordered middleware pipeline handles requests. Modules become middleware, and handlers become endpoints. `Application_Error` becomes `UseExceptionHandler` with Problem Details. And `Application_EndRequest` cleanup, like disposing a per-request database context, disappears, because a scoped service is disposed with the request scope.

**Interviewer:** Minimal APIs or controllers?

[pause 5s]

Both are fully supported. Minimal APIs have less ceremony, and `TypedResults` with a `Results` union give accurate OpenAPI metadata, so they're a good fit for small vertical slices. Controllers are familiar to a Web API 2 team and have rich conventions and filters. I'd pick one per service, record the decision, and be consistent. In FeeBilling, the first slice used minimal APIs, so that's the reference.

**Interviewer:** What happens to `HttpResponseMessage` and `IHttpActionResult`?

[pause 5s]

There's no direct equivalent; the compatibility shim was removed in ASP.NET Core 3.0. I'd return `TypedResults`, or `ActionResult` of T in controllers. A ported method that still returns an `HttpResponseMessage` compiles, but ASP.NET Core serializes it as an object instead of sending it as the response, so each one has to be redesigned. In FeeBilling, the invoice export becomes a file or stream result.

**Interviewer:** How does model binding differ?

[pause 5s]

Web API 2 binds simple types from the URI and one complex type from the body. ASP.NET Core infers sources by convention and also binds services, the user and the cancellation token. The bigger risk is validation: with the API controller attribute, ASP.NET Core returns an automatic 400 in the validation problem details format, while legacy returned a `Message` and `ModelState` object. The legacy AngularJS client reads `Message`, so I'd contract-test error responses too.

**Interviewer:** How do you get the current user without `HttpContext.Current`?

[pause 5s]

Pass it in. Minimal API handlers can bind a `ClaimsPrincipal` parameter, and controllers have `User`. Then pass the user or the relevant claim explicitly to whatever service needs it. `IHttpContextAccessor` exists, but it's the same ambient-state pattern, so I'd keep it rare and never use it in domain code.

## Recap

Five things to remember from this lesson.

One: `Global.asax` becomes `Program.cs`. Registration on the builder, then an explicitly ordered middleware pipeline, with the exception handler first.

Two: modules become middleware, and handlers become endpoints. Order is now your responsibility.

Three: Web API 2 becomes minimal APIs with `MapGroup`, `TypedResults` and a `Results` union. `HttpResponseMessage` has no equivalent and must be redesigned.

Four: error shapes and `Web.config` behaviours like per-request culture and session break silently. Contract-test errors, and decide deliberately.

Five: authorization moves to `RequireAuthorization` or a fallback policy with anonymous health checks, and the user is passed in, not read from ambient state.

In the next lesson, we'll look at dependency injection and ambient state: replacing Unity, removing `HttpContext.Current`, and safely using a scoped database context from a background service.
