# 22 · AngularJS to Angular: Route-Level Strangler

Welcome. A .NET modernization almost always drags the front end along with it, and fullstack interviews probe that directly. FeeBilling's user interface is an AngularJS 1.6 single-page application, and AngularJS has been end of life since January 2022. By the end of this lesson, you should be able to explain how you'd migrate it incrementally, why a route-level strangler beats a hybrid bootstrap for this codebase, why the gateway can't route hash URLs, and how the new app authenticates without keeping tokens in the browser.

## The questions this lesson answers

Here are the questions this lesson prepares you for. How do you migrate an AngularJS app incrementally? ngUpgrade or route-level? How does the single-page app authenticate without storing tokens? How do you keep the front end and the API contracts in sync? And how do you show fifty thousand rows?

The billing screens depend on three things the modern stack changes by default: PascalCase JSON property names, the shape of a serialized .NET `DataSet`, and hash-based URLs. Keep those three in mind. Each one is a place where a migrated screen can break silently.

## A tour of the legacy front end

Start with the shell page, `legacy/FeeBilling.Web/Views/Home/Index.cshtml`. It's a Razor view that loads the AngularJS app. The navigation links point at hash-bang URLs: the address ends in a hash sign, an exclamation mark, and then slash billing, or slash schedules, or slash accounts. Below that, the page loads the app's scripts one by one, with a comment that says order matters: module, then routes, then services, then controllers, and no bundling. There's no build step at all. The shared layout pulls AngularJS 1.6, jQuery and Bootstrap 3 from a CDN.

Next, `legacy/FeeBilling.Web/App/app.module.js`. It sets the location provider's hash prefix to an exclamation mark, because AngularJS 1.6 changed the default and the links in the shell depend on it. It also adds three cache-busting headers to every GET request, including an If-Modified-Since date from 1997, because Internet Explorer 11 cached Web API responses and the billing run list never updated. That's ticket FB-219.

Then `app.routes.js`, which is an `ngRoute` table. There's a comment worth knowing: the "new schedule" route has to be registered before the "schedule by ID" route, or the ID parameter matches the word "new".

The API service, `App/services/api.service.js`, is where the contract assumptions live. The base URL is a relative "api slash", with a comment that it only works if the shell is served from the site root. The start-run method's comment says the response is a camelCase run ID, because the server returns an anonymous object. The invoices method's comment says it returns a serialized `DataSet`, with the rows under a property called `Table`. And there's FB-279: POSTs don't send the anti-forgery token.

Now the screen we're migrating: Billing Run Review. `App/billing/billing-review.controller.js` hard-codes the list of firms, with a to-do, FB-204, to load them from an API. It has a start-run button, and a comment, FB-247: people double-click this and we get two runs for the same period. It polls the runs endpoint every five seconds while anything is pending or running. And at the bottom, there's a note, FB-262: the poller is not cancelled when you leave the page, so it keeps calling the API in the background until the run finishes.

The invoice list, `run-invoices.controller.js` and its template, loads every invoice for the run in one request, reads them from `data.Table`, with a comment, FB-238, that if anyone names the data table on the server, the page silently shows nothing. The template renders every row with `ng-repeat` and filters them in the browser. And the total at the bottom doesn't follow the filter box. For a big firm, that's forty thousand rows in the DOM.

So the migration isn't just a rewrite in a new framework. It's also a chance to fix pagination, polling, idempotency and contracts, deliberately.

## ngUpgrade versus a route-level strangler

There are two ways to migrate AngularJS incrementally.

ngUpgrade is the hybrid approach. Both frameworks run in the same page, with one bootstrap. AngularJS services can be upgraded for Angular to use, and Angular components can be downgraded for AngularJS templates to use. State is shared in memory, and navigation between old and new screens is in-app. The costs are real: both frameworks are in the bundle, the AngularJS digest cycle and Angular's change detection interact, and old and new deploy together. And here, the legacy scripts aren't bundled at all, so they'd first have to join the Angular build.

A route-level strangler is the same pattern we've used for the APIs. The new Angular app is a separate application. It owns whole screens on real paths, and the gateway routes those paths to it. AngularJS keeps every other screen. The two apps share nothing in memory; they share only the server, through APIs and the authentication cookie. Crossing from one to the other is a full page load. In exchange, they deploy independently, and there's no two-framework bootstrap.

Which one fits? ngUpgrade fits when screens are tightly interwoven and must share in-memory state. A route-level strangler fits when screens are separate routes. FeeBilling's screens are separate routes: billing review, run invoices, schedules, accounts. So the one-sentence answer is: route-level, because the screens are separate, it gives independent deploys, and it avoids a hybrid bootstrap. Write that down as an ADR. And if you ever recommend ngUpgrade, check first whether it's still supported in the Angular version you're targeting.

## The hash-routing problem

Here's the non-obvious part, and it's the kind of detail that tells an interviewer you've actually done this.

In a URL, everything after the hash sign is the fragment. Browsers never send the fragment to the server. So when a user opens the billing screen, the request that reaches the gateway is just for the root path, slash. The part that says "billing" stays in the browser, and AngularJS reads it on the client.

That means YARP can't route one hash screen to a new app. From the gateway's point of view, every AngularJS screen is the same URL. You can prove it in a demo: run the gateway, open the billing screen, and both the gateway's log and the browser's network tab show a request for slash only.

So the design follows. The new Angular app owns real paths, for example `/billing-review`, and the gateway gets a route for that path prefix, added ahead of the fallback catch-all route in `src/FeeBilling.Gateway/appsettings.json`. The legacy navbar links in `Index.cshtml` change from hash links to full-page links to the new paths.

Old bookmarks still exist, and they still arrive as fragments the server can't see. So the redirect has to happen on the client. In `app.routes.js`, the old run-invoices route gets a tiny controller that replaces the window location with the new path, carrying the run ID across.

Real paths create one server-side job that hash routing never needed. When a user bookmarks slash billing-review slash runs slash forty-two and opens it later, that full path now reaches the server. Whatever hosts the Angular app, whether a static file host behind the gateway or a small ASP.NET Core app, must answer any path under the app's prefix with the app's index page, and let the Angular router take it from there. Without that fallback, deep links and browser refreshes return 404. It's the mirror image of the hash problem: fragments hide the route from the server, and real paths hand it to the server, which then has to know what to do with it.

Two smaller details. The Angular app needs a base href that matches its gateway path, so its own router and assets resolve correctly. And its API base URL must be absolute, starting with a slash. Legacy's relative "api slash", evaluated from a page at slash billing-review slash runs slash forty-two, would resolve to a path under billing-review, which doesn't exist.

## Building the screen with current Angular

Now build the migrated Billing Run Review screen with current Angular idioms. Avoid pinning version numbers in an interview; talk about the features.

It's a standalone component, so there's no NgModule. State is held in signals: the current run, the invoice rows, and an approving flag. The template uses built-in control flow, `@if` and `@for`, instead of the old structural directives.

The HTTP client isn't hand-written. Billing.Api publishes an OpenAPI document, which ASP.NET Core can generate with the `Microsoft.AspNetCore.OpenApi` package, and CI generates a typed TypeScript client from it with a tool such as NSwag or openapi-generator. That's how you keep the front end and API contracts in sync: when the API changes shape, the client regenerates, and the build fails instead of the page rendering undefined at runtime. One caveat from lesson eleven: the generated client trusts the schema, not the wire. If the OpenAPI document says camelCase and the API actually serializes PascalCase, the client compiles cleanly and renders blanks. Make the document and the serializer agree, and prove it with a test.

Fifty thousand rows need two things. The server pages them, with the cursor paging from lesson twelve. And the browser virtualizes them, so only the visible rows exist in the DOM. The Angular CDK's virtual scroll viewport does that, using its `cdkVirtualFor` directive rather than the built-in `@for`, because built-in control flow doesn't virtualize. Check that detail for your version. What you never do is render the whole set and filter it client-side, which is exactly what legacy does.

Polling gets fixed properly. An RxJS timer polls every five seconds, stops by itself when the run is no longer pending or running, and is piped through `takeUntilDestroyed`, which uses the component's `DestroyRef` so the subscription ends when the component goes away. That's FB-262 fixed.

Two more legacy shortcuts go away with the rewrite. The hard-coded firm list, FB-204, becomes an API call, and that call is scoped by the caller's firm claim from lesson twenty, so a firm user sees only their own firm and vendor staff see all of them. And the invoice total that ignored the filter box becomes a server-side total for whatever filter is applied, returned alongside the page of rows. A client-side sum over one page of fifty thousand rows would be wrong in a new way.

Dates deserve a moment too. Lesson eleven showed how a date without a time zone marker gets interpreted as local time in the browser. The new screen should receive UTC timestamps with an explicit offset, and date-only values like the period end as plain dates, and format them for display in one place.

And the double-click, FB-247, gets fixed properly too. Disabling the start button while a request is pending is good user experience, but it isn't the fix. The fix is server-side idempotency from lesson twelve: the client generates one `Idempotency-Key` per user action, and the server returns the original run for a repeated key. Do both.

## Authentication through the gateway

Because the new app is served through the same gateway as the APIs, it's same-origin. That means the gateway's authentication cookie, from lesson twenty, flows with every request with no code at all. There are no tokens in `localStorage`, where any cross-site scripting bug could read them.

But cookies bring back cross-site request forgery, and FB-279 is still open. So add XSRF protection. The server issues a readable cookie named `XSRF-TOKEN`. Angular's `HttpClient` reads it and echoes it in an `X-XSRF-TOKEN` header, and the server validates it with ASP.NET Core's antiforgery services. Two gotchas. Angular only attaches that header to mutating requests with relative URLs, so an absolute URL silently skips it. And the built-in antiforgery middleware only validates some endpoint types automatically, so for JSON endpoints you may need explicit validation, for example an endpoint filter that calls `ValidateRequestAsync`. Verify the behaviour for your ASP.NET Core version.

Finally, when a call returns 401, the app does a full-page navigation to sign in, rather than showing an in-app error, because the gateway owns the sign-in flow.

## Continuity and end-to-end tests

Users will cross between two apps many times a day. If the navbar, colours and typography change on one click, they experience the boundary as "the site broke". So share design tokens, such as CSS variables for colours and spacing, and keep the navbar identical in both.

Cover the journey end to end with Playwright: start a run, watch its progress, review the invoices, approve. That journey crosses from AngularJS into Angular and back, which is exactly where regressions hide. Write it against the current legacy app first, get it green, and keep it green as the screen moves.

## Traps

Planning to route hash URLs at the proxy. Fragments never leave the browser. The split happens on real paths or not at all.

Relative API URLs under a sub-path, which resolve to paths that don't exist.

Porting FB-247 as "disable the button". That's UX; the fix is idempotency on the server.

Using `@for` over fifty thousand rows. Use the virtual scroll viewport, and page on the server.

Tokens in `localStorage`. And the flip side: once you use cookies, XSRF protection is mandatory.

Casing drift between the OpenAPI document and the real JSON.

Copying the Internet Explorer cache-busting hack into the new app. Solve caching on the server with proper Cache-Control headers.

And two different-looking apps.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do you migrate an AngularJS app incrementally?

[pause 5s]

With a route-level strangler behind the same gateway as the APIs. The new Angular app owns whole screens on real paths, and AngularJS keeps the rest. Links across the boundary become full-page navigations, and old hash bookmarks get a client-side redirect, because the server never sees fragments. I'd migrate one screen fully, tests included, before starting the next, and cover the cross-app journey with Playwright.

**Interviewer:** ngUpgrade or route-level?

[pause 5s]

ngUpgrade when screens are tightly interwoven and must share in-memory state. Route-level when screens are separate routes. Route-level gives independent deploys and avoids running both frameworks in one page, with their change detection interacting. In FeeBilling, the screens are separate and the legacy scripts aren't even bundled, so route-level wins, and I'd record that in an ADR.

**Interviewer:** How does the single-page app authenticate without storing tokens?

[pause 5s]

The gateway acts as a backend for frontend. It runs the OIDC flow and holds the tokens. The SPA is same-origin, so the browser sends the HttpOnly session cookie automatically, and JavaScript never touches a token. Cookies bring back cross-site request forgery, so the server issues an XSRF token cookie, Angular's HTTP client echoes it in a header on mutating requests, and the server validates it, together with SameSite cookies.

**Interviewer:** How do you keep the front end and API contracts in sync?

[pause 5s]

Generate the TypeScript client from the API's OpenAPI document in CI, so contract drift fails the build instead of producing undefined values at runtime. And test that the OpenAPI document matches what the serializer actually emits, because a generated client trusts the schema, not the wire. PascalCase versus camelCase is the classic mismatch.

**Interviewer:** How do you show fifty thousand rows?

[pause 5s]

Page on the server with cursor paging, and virtualize in the browser with the CDK virtual scroll viewport, so only the visible rows are in the DOM. Never load the whole set and filter it client-side, which is what the legacy screen does with forty thousand invoices.

## Recap

Five things to remember from this lesson.

One: use a route-level strangler for separate screens. The new app owns real paths behind the same gateway, and deploys independently.

Two: the gateway can't see hash URLs, because fragments never reach the server. Route on real paths, change the links, and redirect old bookmarks on the client.

Three: build with current Angular idioms, standalone components, signals and built-in control flow, and generate the API client from OpenAPI instead of remembering the contract.

Four: fix the legacy bugs properly: server paging plus virtual scrolling, polling that stops when the component is destroyed, and idempotency on the server for the double-click.

Five: the SPA uses the gateway's cookie, not tokens, and XSRF protection comes with it.

In the next lesson, we'll look at cutover, shadow runs and decommissioning: how you switch a billing system over one firm at a time, and what rollback means once invoices have been sent.
