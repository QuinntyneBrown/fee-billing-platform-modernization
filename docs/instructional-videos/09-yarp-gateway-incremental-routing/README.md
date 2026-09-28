# 09 · YARP Gateway and Incremental Routing

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work packages:** WP-03 (route the Billing API), WP-10 (remove the fallback) · **Prerequisites:** 01, 07

## Why this video exists

A strangler fig needs a facade that sends each route to either the legacy app or a new service, and the facade must change by *configuration*, not by editing the legacy app. In FeeBilling that facade is `FeeBilling.Gateway`, an ASP.NET Core app hosting YARP. "How do you migrate one route at a time, and how do you roll it back?" is a standard interview question, and the answer is only convincing if you can show the config, the precedence rules, what transforms keep legacy URLs stable, and what the gateway does *not* protect you from.

## Learning objectives

By the end, the viewer can:

- Read and write YARP route and cluster configuration, including route precedence (`Order`) and the `{**catch-all}` fallback.
- Migrate a route and roll it back with a config change, and explain why that rollback doesn't undo data written by the new service.
- Use transforms to keep legacy URLs working when the new API has a different shape (for example `/api/billing` → `/api/v1/billing`).
- Route a subset of traffic (a canary header, a query parameter) to a new service, and name the limits of doing that at the proxy.
- Configure the operational parts: forwarded headers, health checks, session affinity for the legacy farm, and timeouts for long exports.
- Test gateway routing automatically.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do you migrate one route at a time? | A reverse proxy in front of everything. One route per migrated path prefix pointing at the new service's cluster, plus a low-priority catch-all to legacy. Migrating is adding a route; the legacy code for that route stays, frozen, until the route has proven itself. |
| How do you roll back a migrated endpoint? | Remove or re-point the route; YARP reloads config without a restart. Then the caveat: routing rolls back *reads* instantly, but anything the new service wrote must still be readable by legacy, so schema changes during the transition are backward compatible (expand/contract). |
| Why a reverse proxy instead of changing the clients? | Clients (the AngularJS app, partner integrations, bookmarks) keep one stable base URL. Routing becomes a deployable, reviewable config change owned by the migration team, and the gateway becomes the natural home for cross-cutting concerns (auth, correlation IDs, rate limits). |
| Why YARP over IIS ARR or a cloud gateway? | ARR ties you to IIS, which is what you're leaving. Azure Front Door / Application Gateway path routing works in Azure but not on a laptop or in a single-tenant client install. YARP runs anywhere .NET runs, is configured from `appsettings.json`, and is extensible in C#. (ADR-0003.) |
| Doesn't the gateway become a single point of failure? | Yes, so treat it like one: stateless, at least two instances behind a load balancer or platform ingress, health endpoints, and config from a central store. Keep business logic out of it. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `docs/adr/0003-yarp-gateway.md` | Options considered, "migrating a route = add a route entry + deploy" |
| `src/FeeBilling.Gateway/Program.cs` | `AddReverseProxy().LoadFromConfig(...)`, `MapReverseProxy()`, and `public partial class Program;` for tests |
| `src/FeeBilling.Gateway/appsettings.json` | `accounts`, `households` and `fallback` routes; `"Order": 1000`; the two clusters |
| `legacy/FeeBilling.Web/Global.asax.cs` | `.AddProxySupport(options => options.UseForwardedHeaders = true)` |
| `legacy/FeeBilling.Web/Controllers/Mvc/AccountController.cs` | `Request.UserHostAddress` in the failed-login log line: what it logs behind a proxy |
| `legacy/FeeBilling.Web/App/services/api.service.js` | Every relative URL the AngularJS app calls; the routing table must cover all of them |
| `legacy/FeeBilling.Web/Web.config` | `<sessionState mode="InProc" />` and the shared `machineKey` "across the web farm" |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Live: remove the `accounts` route from `appsettings.json` while the gateway runs. The next request goes to legacy, with no restart. That's a five-second rollback. Then ask what it *didn't* roll back. |
| 01:30–04:30 | Why a facade | ADR-0003's three options and why YARP won. Stable client URLs. Who owns routing changes. The gateway as the place for auth, correlation IDs and rate limits later, but never business logic. |
| 04:30–08:00 | Anatomy of the config | Routes (`Match.Path`, `ClusterId`, `Order`), clusters, destinations. `{**rest}` and `{**catch-all}` catch-all parameters. Precedence: explicit `Order` first, then the more specific template. Why the fallback needs `Order: 1000` so it never beats a migrated route. `LoadFromConfig` watches configuration reload tokens, so file edits take effect live. |
| 08:00–11:00 | Migrating and rolling back a route | Add a `billing` route for the future Billing.Api. Walk through what "rolled back" means: legacy `BillingController` still exists (keep it frozen), but if the new API wrote runs or invoices in a shape legacy can't read, routing back doesn't fix it. Link to video 14 (expand/contract). |
| 11:00–14:00 | Transforms and headers | Keep the AngularJS URLs (`api/billing/...`) working while the new API lives at `/api/v1/billing/...` with a `PathPattern` transform. YARP adds `X-Forwarded-For`, `-Proto`, `-Host` and `-Prefix` by default. Legacy calls `AddProxySupport(... UseForwardedHeaders = true)`; without it, every failed login logs the gateway's IP. Host header behaviour matters for IIS host-name bindings (check the defaults for your YARP version). |
| 14:00–16:30 | Partial routing | Route by header (`X-FeeBilling-Canary: true`) or query parameter (`firmId=1`) to the new cluster, with a lower `Order` than the general route. The limit: `POST /api/billing/runs` carries `FirmId` in the body, so per-firm routing for writes needs a claim or a header set upstream, not a proxy rule. Per-firm *behaviour* flags usually belong in the service (video 23). |
| 16:30–18:30 | Operability | Active health checks need a health endpoint on the destination (legacy has none today). Passive health checks. Session affinity if the gateway fronts more than one legacy IIS node, because legacy session is `InProc`. `HttpRequest.ActivityTimeout` defaults to 100 seconds: the legacy invoice export builds a whole `DataSet` before sending the first byte. Trace context propagation from gateway to services (verify what your YARP version propagates). Running at least two gateway instances. |
| 18:30–20:00 | Testing routes, recap | A table-driven test that every path in `api.service.js` lands on the expected cluster. Recap: routing is cheap to change, so make it safe to change (tests, review, health checks). |

### Before: current routing

```json
"Routes": {
  "accounts":   { "ClusterId": "modern-accounts", "Match": { "Path": "/api/accounts/{**rest}" } },
  "households": { "ClusterId": "modern-accounts", "Match": { "Path": "/api/households/{**rest}" } },
  "fallback":   { "ClusterId": "legacy-iis",      "Match": { "Path": "{**catch-all}" }, "Order": 1000 }
}
```

### After: a migrated Billing API with legacy URLs preserved (sketch)

```json
"Routes": {
  "billing-canary": {
    "ClusterId": "modern-billing",
    "Order": 10,
    "Match": {
      "Path": "/api/billing/{**rest}",
      "Headers": [ { "Name": "X-FeeBilling-Canary", "Values": [ "true" ], "Mode": "ExactHeader" } ]
    },
    "Transforms": [ { "PathPattern": "/api/v1/billing/{**rest}" } ]
  },
  "billing-v1": {
    "ClusterId": "modern-billing",
    "Match": { "Path": "/api/v1/billing/{**rest}" }
  },
  "fallback": { "ClusterId": "legacy-iis", "Match": { "Path": "{**catch-all}" }, "Order": 1000 }
},
"Clusters": {
  "modern-billing": {
    "HttpRequest": { "ActivityTimeout": "00:05:00" },
    "Destinations": { "billing-api": { "Address": "http://localhost:5102/" } }
  },
  "legacy-iis": {
    "SessionAffinity": { "Enabled": true, "Policy": "Cookie", "AffinityKeyName": ".FeeBilling.Affinity" },
    "Destinations": { "iis": { "Address": "http://localhost:8080/" } }
  }
}
```

The canary route sends only requests with the header to the new API, translating the legacy path. Everything else under `/api/billing` still falls through to legacy. Promoting the canary means removing the header match. The Billing.Api port is illustrative.

### Route test (sketch)

```csharp
public sealed class GatewayRoutingTests : IAsyncLifetime
{
    private WebApplication _modern = null!;
    private WebApplication _legacy = null!;
    private WebApplicationFactory<Program> _gateway = null!;

    public async ValueTask InitializeAsync()
    {
        _modern = await StartStubAsync("modern");
        _legacy = await StartStubAsync("legacy");

        _gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ReverseProxy:Clusters:modern-accounts:Destinations:accounts-api:Address", _modern.Urls.Single());
            b.UseSetting("ReverseProxy:Clusters:legacy-iis:Destinations:iis:Address", _legacy.Urls.Single());
        });
    }

    [Theory]
    [InlineData("/api/accounts?firmId=1", "modern")]
    [InlineData("/api/households/100", "modern")]
    [InlineData("/api/billing/runs?firmId=1", "legacy")]
    [InlineData("/api/feeschedules", "legacy")]
    public async Task Path_IsRoutedToExpectedCluster(string path, string expected)
    {
        var body = await _gateway.CreateClient().GetStringAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(expected, body);
    }

    // YARP forwards with its own HttpClient, not through TestServer, so destinations must be real sockets.
    private static async Task<WebApplication> StartStubAsync(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Map("/{**path}", () => name);
        await app.StartAsync();
        return app;
    }

    public async ValueTask DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _modern.DisposeAsync();
        await _legacy.DisposeAsync();
    }
}
```

The first row checks that `/api/accounts/{**rest}` also matches `/api/accounts` with no trailing segment, which is exactly what the AngularJS app calls.

## Demo

```bash
docker compose up -d
dotnet run --project src/FeeBilling.Accounts.Api    # :5101
dotnet run --project src/FeeBilling.Gateway         # :5000 (Yarp logging is at Information in appsettings.json)

curl -i "http://localhost:5000/api/accounts?firmId=1"   # proxied to :5101 (500 unless legacy IIS is up: remote auth, video 10)
curl -i "http://localhost:5000/api/feeschedules"        # proxied to :8080; 502 if legacy IIS isn't running
```

1. Point at the gateway log line showing which destination each request went to.
2. With the gateway still running, edit `src/FeeBilling.Gateway/appsettings.json` (under `dotnet run` the content root is the project folder, so this is the file the app reads and watches). Delete the `accounts` route, save, and repeat the first `curl`: it now goes to `legacy-iis`.
3. Restore the route. Discuss what a production rollback looks like: the same change, but through a reviewed pull request or App Configuration, not a hand edit.

## Traps to call out

- **Forgetting `Order` on the fallback.** A catch-all with default order can compete with specific routes; make the fallback explicitly the lowest priority.
- **Assuming route rollback is data rollback.** It's only safe if the new service's writes are legacy-compatible.
- **Hash-fragment URLs.** The AngularJS app uses `#!/billing` routes. Fragments never reach the server, so the gateway can't route UI screens by hash. That drives the front-end strategy in video 22.
- **Per-tenant routing in the proxy.** Tenant IDs in request bodies aren't visible to path/header/query matching. Don't build a body-parsing proxy; move tenant selection to a claim or header, or keep the behaviour flag in the service.
- **Default 100-second activity timeout.** Long exports that compute before streaming fail at the gateway with a 504 while the backend is still working. Fix the export (stream it, video 12) before raising the timeout.
- **Losing the client IP and scheme.** Both sides must honour `X-Forwarded-*`, and the services should only trust them from known proxies.
- **The gateway exposes everything legacy exposes.** The catch-all forwards *every* path to IIS, including internal endpoints such as the SystemWebAdapters remote-app endpoints (video 10). Decide explicitly what the internet can reach.
- **Business logic creeping into the gateway.** Transforms for compatibility are fine; fee rules and tenant decisions are not.

## Key terms

Reverse proxy · route · cluster · destination · catch-all parameter · route order / precedence · transform · canary · session affinity · active and passive health checks · forwarded headers

## After the video

1. Write the gateway route table for the end state of WP-03: every `api.service.js` call, which cluster serves it, and which transform (if any) it needs.
2. Add a health endpoint to the plan for legacy IIS (a simple handler is enough) so the gateway can run active health checks against it.
3. Draft the gateway section of the cutover runbook: who can change routes, how the change is reviewed, and how it's verified.

## References

- `docs/adr/0003-yarp-gateway.md`, `docs/adr/0001-strangler-fig-migration.md`
- `docs/brasswick-modernization-training-plan.md`: Section 7 (gateway routing), WP-03, WP-10
- YARP documentation (Microsoft Learn): *Configuration files*, *Request and response transforms*, *Header routing*, *Destination health checks*, *Session affinity*, *HTTP client configuration* (verify defaults such as `ActivityTimeout` and Host header handling for the YARP version you use)
