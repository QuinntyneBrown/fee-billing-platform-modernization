# 09 · YARP Gateway and Incremental Routing

Welcome. A strangler fig needs a front door: one place that decides, for every request, whether the legacy application or a new service handles it. In FeeBilling that front door is `FeeBilling.Gateway`, an ASP.NET Core application hosting YARP, Microsoft's reverse proxy library. This lesson teaches you how that routing works, how you migrate one route and roll it back, how you keep old URLs working when the new API has a different shape, and what the gateway does not protect you from. By the end, you should be able to answer the routing questions with configuration, precedence rules and caveats, not just the word "proxy."

## The questions this lesson answers

Here are the questions this lesson prepares you for. How do you migrate one route at a time? How do you roll back a migrated endpoint? Why put a reverse proxy in front instead of changing the clients? Why YARP rather than IIS Application Request Routing or a cloud gateway? And the operational one: doesn't the gateway become a single point of failure?

A good answer to all of these has two halves. The first half is mechanics: routes, clusters, order, transforms. The second half is limits: what a routing change can undo, and what it can't.

## Why a facade at all

Start with the decision record. Open `docs/adr/0003-yarp-gateway.md`. The context says the strangler fig needs a single entry point that can send each route to either the legacy IIS application or a new service, and that changing routing should be a configuration change, not a code change in the legacy app.

The ADR considered three options. IIS URL Rewrite with Application Request Routing, on the legacy servers. That keeps you tied to IIS, which is exactly what you're leaving. Azure Front Door or Application Gateway path routing. That's fine in Azure, but it doesn't help local development, and it doesn't help a single-tenant client install on a client's own servers. And YARP, which runs anywhere .NET runs, is configured from `appsettings.json`, and can be extended in C# when you need transforms, authentication or correlation IDs.

Why not just change the clients to call the new services directly? Because the clients are many and slow to change: the AngularJS application, partner integrations, bookmarks, scripts somebody wrote years ago. With a gateway, they keep one stable base URL. Routing becomes a deployable, reviewable configuration change owned by the migration team. And the gateway becomes the natural home for cross-cutting concerns later: authentication, correlation IDs, rate limits. What it must never become is a home for business logic. Fee rules and tenant decisions belong in services.

## Anatomy of the configuration

Now open `src/FeeBilling.Gateway/Program.cs`. It's short. It calls `AddReverseProxy`, then `LoadFromConfig` with the `ReverseProxy` section of configuration, and then `MapReverseProxy` to plug the proxy into the request pipeline. At the bottom there's a line declaring `public partial class Program`, which exists so integration tests can host the gateway with `WebApplicationFactory`.

Then open `src/FeeBilling.Gateway/appsettings.json`. YARP configuration has two parts: routes and clusters.

A route says which requests it matches and which cluster should handle them. There are three routes. The accounts route matches the path slash api slash accounts, followed by a catch-all parameter called rest, which means "anything after this prefix, including nothing." It points at the cluster called modern-accounts. The households route does the same for slash api slash households. The third route, called fallback, matches every path with a catch-all parameter and points at the cluster called legacy-iis. It also sets an Order of one thousand.

A cluster is a named group of destinations, which are the actual addresses. The modern-accounts cluster has one destination, the new `FeeBilling.Accounts.Api` on localhost port 5101. The legacy-iis cluster has one destination, the IIS application on localhost port 8080.

Notice that the accounts route's catch-all also matches the bare prefix. The AngularJS app calls api slash accounts with only a query string, a firm ID of one, and no trailing segment. A catch-all parameter matches zero or more segments, so that request still lands on the new API. It deserves a test of its own, because it's exactly the call the front end makes.

Precedence is the part people get wrong. YARP routes are ASP.NET Core endpoints, so the usual endpoint rules apply. An explicit Order wins first, and a lower number means higher priority. When order doesn't separate two routes, the more specific route template wins. The fallback's Order of one thousand makes it explicitly the lowest priority, so it can never beat a migrated route, no matter how the templates compare. If you ever write a catch-all without that, you're relying on specificity rules to save you.

One more detail makes live rollback possible. `LoadFromConfig` listens to configuration reload notifications, and `appsettings.json` is reloaded on change by default. So if you edit the routes while the gateway is running, the new routing takes effect without a restart.

The same file sets the log level for the Yarp category to Information. At that level, YARP logs which destination each request was proxied to. That's the first thing you look at when someone says a route isn't working.

## Migrating a route, and what rollback really means

Here's the migration move in its simplest form. When the Billing API from work package three exists, you add a billing route that matches slash api slash billing and points at a new cluster for the Billing API. You deploy it. Requests for billing now go to the new service, and everything else still falls through to legacy.

And here's rollback in its simplest form. Remove the route, or point it back at legacy. The next request goes to IIS. If you do that live in a demo, deleting the accounts route while the gateway runs, the very next request to slash api slash accounts lands on legacy. That's a five-second rollback.

Now the caveat, which is the part that makes your answer senior. Routing rolls back reads instantly. It does not roll back data. If the new Billing API has already written billing runs or invoices, legacy has to be able to read them after you route back. That only works if every schema change during the transition is backward compatible, using the expand-and-contract approach from lesson one, and in more detail in lesson fourteen. So the rollback plan for a writing service is really a data compatibility plan with a routing change on top.

In practice, a careful migration of one route has a few stages. Deploy the new service dark, running but not routed, and run contract tests against it directly. Then send a canary slice of traffic to it and watch error rates, latency and parity checks. Then route everything. Keep the legacy path frozen for at least one full cycle of whatever the route does, and only then delete it. Each stage is a small, reversible configuration change.

This is also why the legacy controller for a migrated route stays in place, frozen. `legacy/FeeBilling.Web/Controllers/Api/AccountsController.cs` carries the comment: migrated, kept for rollback, do not add features here. It's the thing you route back to. Once the new route has proven itself, you delete it.

## Transforms, headers and partial routing

Sometimes the new API doesn't have the same URL shape as the old one. Say the new Billing API is versioned and lives under slash api slash v1 slash billing, but the AngularJS app still calls api slash billing. Open `legacy/FeeBilling.Web/App/services/api.service.js`: every call uses a relative base URL of api slash, so the app calls api slash billing slash runs, api slash feeschedules, and so on. You don't want to change the front end yet. YARP transforms solve this. A route can carry a path pattern transform that rewrites the incoming path into the new shape before forwarding. The client keeps its old URL; the service gets its new one. That's compatibility work, and it's a legitimate use of the gateway.

There are simpler transforms too: removing a path prefix, adding one, or setting a request header. Keep the set small. Every transform is a place where the URL the client sees and the URL the service sees differ, and the next person to read a log has to understand that difference.

Headers matter too. When YARP forwards a request, it adds the forwarded headers by default: `X-Forwarded-For` with the client's address, and the matching headers for protocol, host and path prefix. The destination has to honour them. Legacy does: in `legacy/FeeBilling.Web/Global.asax.cs`, the SystemWebAdapters setup calls `AddProxySupport` with `UseForwardedHeaders` set to true. Why does that matter? Look at `legacy/FeeBilling.Web/Controllers/Mvc/AccountController.cs`. When a login fails, it logs the user name and `Request.UserHostAddress`. Behind a proxy that doesn't honour forwarded headers, every failed login would log the gateway's address, and your brute-force investigation would point at your own infrastructure. The services should only trust those headers from known proxies, or anyone can spoof them.

You can also route part of the traffic. A route can match on a header or a query parameter as well as a path. So you might add a canary route that sends billing requests to the new service only when a header like X-FeeBilling-Canary is set to true. Give it a lower Order number than the general route, so it's checked first, and everything without the header still falls through to legacy. Promoting the canary means removing the header match.

Know the limit, though. The request to start a billing run, a POST to api slash billing slash runs, carries the firm ID in the JSON body. A proxy's path, header and query matching can't see the body, and you don't want to build a body-parsing proxy. So per-firm routing for writes needs the firm to be in a claim or a header set upstream. More often, per-firm behaviour belongs in the service, behind a feature flag, which is how lesson twenty-three handles cutover.

## Operability

Because every request goes through it, the gateway has to be treated as critical infrastructure. It should be stateless, run as at least two instances behind a load balancer or platform ingress, expose health endpoints, and read its configuration from a central, reviewed source rather than a hand-edited file. In production, a rollback is the same route change, but made through a reviewed pull request or a configuration store like Azure App Configuration.

YARP can check destination health. Active health checks probe an endpoint on each destination on a schedule, which requires the destination to have one; legacy IIS doesn't have a health endpoint today, so adding a simple one belongs in the plan. Passive health checks watch real traffic for failures.

Two legacy details affect cluster settings. First, legacy session state is in-process: `legacy/FeeBilling.Web/Web.config` sets the session state mode to InProc. If the gateway fronts more than one legacy IIS node, you need session affinity, so a user keeps landing on the node that holds their session. Second, the same file shares a machine key across the web farm, which is what lets the Forms authentication cookie work on any node.

One more header detail. Whether the gateway passes the original Host header through or replaces it with the destination's address depends on configuration and on the YARP version, so check the defaults for yours. It matters because IIS sites often bind by host name, and a request with an unexpected Host header can land on the wrong site or none at all.

Timeouts are the last one. YARP has a request activity timeout, which in current versions defaults to about a hundred seconds; check the default for the version you use. The legacy invoice export builds a whole data set in memory before it sends the first byte, so a big firm's export can hit that timeout at the gateway and return a gateway timeout while the backend is still working. The right fix is to stream the export, which lesson twelve covers, not to raise the timeout forever.

Finally, test the routing. Because `Program` is exposed, a test can host the gateway with `WebApplicationFactory`, point the two clusters at tiny stub servers that each reply with their own name, and assert that each path from `api.service.js` lands on the expected cluster. One subtlety: YARP forwards with its own HTTP client, not through the in-memory test server, so the stub destinations have to listen on real sockets. A table-driven test like that turns every routing change into something a reviewer can trust.

## Traps

Here are the traps.

Forgetting the Order on the fallback. A catch-all without an explicit low priority can compete with specific routes.

Assuming route rollback is data rollback. It's only safe if the new service's writes are legacy compatible.

Hash-fragment URLs. The AngularJS app uses routes like hash bang slash billing. The fragment never reaches the server, so the gateway can't route UI screens by hash. That shapes the front-end strategy in lesson twenty-two.

Per-tenant routing in the proxy. Tenant IDs in request bodies are invisible to path, header and query matching.

The default activity timeout. Long exports that compute before streaming fail at the gateway.

Losing the client IP and scheme, by not honouring forwarded headers, or by trusting them from anyone.

Exposing everything legacy exposes. The catch-all forwards every path to IIS, including internal endpoints like the SystemWebAdapters remote-app endpoints from lesson ten. Decide explicitly what the internet can reach.

And business logic creeping into the gateway. Compatibility transforms are fine. Fee rules are not.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do you migrate one route at a time?

[pause 5s]

I put a reverse proxy in front of everything; in FeeBilling that's YARP in `FeeBilling.Gateway`. There's one route per migrated path prefix pointing at the new service's cluster, and a catch-all fallback to legacy with an explicit low priority, an Order of one thousand. Migrating a route means adding a route entry and deploying. The legacy code for that route stays in place, frozen, until the new route has proven itself, and then it's deleted.

**Interviewer:** How do you roll back a migrated endpoint?

[pause 5s]

Remove or re-point the route. YARP picks up configuration changes without a restart, so it takes seconds. But routing only rolls back reads. If the new service has written data, legacy must still be able to read it, so during the transition every schema change is backward compatible, using expand and contract. For a service that writes, the rollback plan is really a data compatibility plan.

**Interviewer:** Why a reverse proxy instead of changing the clients?

[pause 5s]

The clients, the AngularJS app, partner integrations, bookmarks, keep one stable base URL, and they're slow to change. Routing becomes a reviewable configuration change owned by the migration team, and the gateway is the natural home for cross-cutting concerns like authentication, correlation IDs and rate limits. Transforms can keep legacy URLs working when the new API has a versioned path.

**Interviewer:** Why YARP over IIS ARR or a cloud gateway?

[pause 5s]

ARR ties you to IIS, which is what you're migrating away from. A cloud gateway like Front Door works in Azure but not on a developer laptop or in a single-tenant client install. YARP runs anywhere .NET runs, is configured from `appsettings.json`, reloads configuration live, and is extensible in C#. That's the reasoning in the project's ADR-0003.

**Interviewer:** Doesn't the gateway become a single point of failure?

[pause 5s]

Yes, so treat it like one. Keep it stateless, run at least two instances behind a load balancer or ingress, give it health endpoints and health checks on its destinations, and load configuration from a reviewed central store. Keep business logic out of it, so it stays simple enough to be reliable. And test routing automatically, so a bad route change is caught before it ships.

## Recap

Five things to remember from this lesson.

One: routes match requests and point at clusters; clusters hold destinations. In FeeBilling, accounts and households go to the new API, and everything else falls through to legacy IIS.

Two: the fallback has an Order of one thousand, so it's always the lowest priority. Lower numbers win.

Three: migrating is adding a route, rolling back is removing it, and YARP reloads configuration live. But routing rolls back reads, not data.

Four: transforms keep legacy URLs working, forwarded headers keep client addresses honest, and partial routing by header works for canaries but can't see tenant IDs in request bodies.

Five: the gateway is critical infrastructure. Run more than one, health-check its destinations, watch the timeouts, test the routes, and keep business logic out of it.

In the next lesson, we'll look at how the old and new applications share a logged-in user, using SystemWebAdapters remote authentication, and what that coupling costs.
