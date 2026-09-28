# 22 · AngularJS to Angular: Route-Level Strangler

> **Runtime:** ~20 min · **Level:** Senior / SME (fullstack) · **Work package:** WP-09 · **Prerequisites:** 09, 11, 12, 20

## Why this video exists

A .NET modernization almost always drags the front end along, and fullstack interviews probe it directly. FeeBilling's UI is an AngularJS 1.6 SPA (end of life since January 2022). It's served by a Razor view, loaded as unbundled script tags, routed with `#!` hash URLs, and written against PascalCase JSON and a serialized `DataSet`. This video migrates one screen, Billing Run Review, to current Angular behind the YARP gateway, without a hybrid bootstrap and without breaking the screens that stay on AngularJS. The non-obvious part is routing: **the gateway can't see hash URLs.**

## Learning objectives

By the end, the viewer can:

- Compare ngUpgrade (hybrid) with a route-level strangler, and justify the choice for this codebase.
- Explain why hash-based routes can't be split at a reverse proxy, and design paths, links and bookmark redirects around that.
- Build the migrated screen with current Angular idioms: standalone components, signals, built-in control flow, and a typed client generated from OpenAPI.
- Render 50,000 invoice rows with virtual scrolling, and fix the legacy polling leak and double-click bug properly.
- Authenticate the SPA through the gateway's cookie (BFF) with XSRF protection, and no tokens in the browser.
- Cover the journey end to end with Playwright, across the old/new boundary.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do you migrate an AngularJS app incrementally? | A route-level strangler behind the same gateway as the APIs. The new Angular app owns whole screens on real paths; AngularJS keeps the rest. Links across the boundary are full-page navigations. Migrate one screen fully (tests included) before the next. |
| ngUpgrade or route-level? | ngUpgrade when screens are tightly interwoven and must share in-memory state. Route-level when screens are separate routes. It gives independent deploys and no two-framework bootstrap or change-detection interplay. Here the screens are separate routes and the legacy scripts aren't even bundled, so route-level wins. |
| How does the SPA authenticate without storing tokens? | BFF: the gateway runs OIDC and holds the tokens; the SPA is same-origin and sends the `HttpOnly` session cookie. Cookies bring CSRF back, so use antiforgery via the `XSRF-TOKEN` cookie and `X-XSRF-TOKEN` header, plus `SameSite`. |
| How do you keep the front end and API contracts in sync? | Generate the TypeScript client from the API's OpenAPI document in CI. Drift fails the build instead of producing `undefined` at runtime. |
| How do you show 50,000 rows? | Server-side cursor paging (video 12) plus virtual scrolling, so only visible rows are in the DOM. Never render the whole set and filter it client-side, which is what legacy does. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Web/Views/Home/Index.cshtml` | `href="#!/billing"` links; unbundled `<script>` tags ("Order matters ... No bundling"); `window.feeBillingConfig` |
| `legacy/FeeBilling.Web/App/app.module.js` | `$locationProvider.hashPrefix('!')`; IE11 cache-busting headers (FB-219) |
| `legacy/FeeBilling.Web/App/app.routes.js` | `ngRoute` table; the "register `/schedules/new` before `/schedules/:id`" comment |
| `legacy/FeeBilling.Web/App/services/api.service.js` | Relative `'api/'` base URL; `{ runId: 123 }` camelCase exception; `{ "Table": [...] }`; FB-279 anti-forgery TODO |
| `legacy/FeeBilling.Web/App/billing/billing-review.controller.js` | Hard-coded firms (FB-204); double-click (FB-247); poller never cancelled (FB-262) |
| `legacy/FeeBilling.Web/App/billing/billing-review.html` | Bootstrap 3 markup, glyphicons, `run.Status` PascalCase bindings |
| `legacy/FeeBilling.Web/App/billing/run-invoices.controller.js`, `run-invoices.html` | `data.Table \|\| []` (FB-238); `ng-repeat ... \| filter:vm.filterText` over the whole run; total that ignores the filter |
| `src/FeeBilling.Gateway/appsettings.json` | Where the new SPA route goes, ahead of the `fallback` catch-all |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | AngularJS has been end of life since January 2022. The billing screens depend on three things the new stack changes by default: PascalCase JSON, a `DataSet` shape, and hash URLs. |
| 01:30–04:30 | Legacy tour | Script tags whose order matters, and no bundler. `hashPrefix('!')`. IE11 cache-busting headers on every GET. A relative `api/` base that only works from the site root. Hard-coded firms. A 5-second poller that outlives the page. A double-clickable "Start billing run". Every invoice for the run in one `ng-repeat`, filtered in the browser. |
| 04:30–07:30 | ngUpgrade vs route-level | Walk the comparison table below. Decide route-level and say why in one sentence: separate screens, independent deploys, no hybrid bootstrap. Check whether ngUpgrade is still supported in your target Angular version before recommending it anywhere. The output is an ADR. |
| 07:30–10:30 | The hash-routing problem | Browsers never send the `#!/billing` fragment to the server, so YARP only ever sees `/`, and it can't route one screen to a new app. The new app owns real paths (`/billing-review/...`), and the legacy navbar links become full-page links to them. Old bookmarks (`/#!/billing/runs/42`) need a client-side redirect in AngularJS, because the server can't see them. `<base href="/billing-review/">`, and an absolute `/api/` base, because legacy's relative `api/` would resolve under `/billing-review/`. |
| 10:30–14:30 | Build the screen | A standalone component with signals and `@if`/`@for`. A typed client generated from Billing.Api's OpenAPI document. Invoices in a CDK virtual scroll viewport: it uses `*cdkVirtualFor`, not `@for` (verify for your version). Polling with `takeUntilDestroyed`, which fixes FB-262. A "Start" button disabled while pending, with one `Idempotency-Key` per user action (video 12), which fixes FB-247 for real. |
| 14:30–17:00 | Auth through the gateway | Same origin, so the BFF cookie flows with no code. No tokens in `localStorage`. XSRF: the server issues an `XSRF-TOKEN` cookie; Angular's `HttpClient` echoes it as `X-XSRF-TOKEN` on mutating, relative-URL requests, and the server validates it. A 401 triggers a full-page navigation to sign-in, not an in-app error. |
| 17:00–19:00 | Continuity and E2E | Users cross between two apps, so keep the navbar, colours and typography the same (shared CSS tokens), or it feels like two products. Playwright journey: start run, watch progress, review invoices, approve. It crosses from AngularJS into Angular and back. |
| 19:00–20:00 | Recap | Route-level strangler, real paths instead of fragments, contracts generated rather than remembered, cookie plus XSRF instead of tokens. |

### ngUpgrade vs route-level strangler

| | ngUpgrade (hybrid) | Route-level strangler |
|---|---|---|
| Runtime | Both frameworks in one page, one bootstrap | Separate apps, each owning its routes |
| Deploys | Together | Independent |
| Sharing state | In memory (upgraded and downgraded services) | Only through the server (API, cookie) |
| Change detection | AngularJS digest and Angular change detection interacting | Not an issue |
| Crossing the boundary | In-app navigation | Full page load |
| Build impact here | Legacy unbundled scripts must join the Angular build | Legacy untouched except for links and bookmark redirects |
| Fits when | Screens are interwoven | Screens are separate routes (FeeBilling) |

### Before: what the new screen must replace

```js
        $locationProvider.hashPrefix('!');
```

```js
        // Relative on purpose so it works under an IIS virtual directory. Only works if the shell
        // is served from the site root (/), not /Home/Index.
        var baseUrl = 'api/';

        // TODO FB-279: POSTs don't send the anti-forgery token. Web API doesn't check it anyway.
```

```js
            // The server sends back the whole DataSet, so the rows are under "Table" (FB-238).
            // If anyone names the DataTable on the server this page silently shows nothing.
            return api.getRunInvoices(runId).then(function (data) {
                vm.invoices = data.Table || [];
```

```js
        // NOTE: the poller is not cancelled when you leave the page. It keeps calling
        // api/billing/runs every 5s in the background until the run finishes. (FB-262)
```

### After: routing at the gateway, and the bookmark redirect (sketch)

```json
"Routes": {
  "billing-review": { "ClusterId": "spa-billing", "Match": { "Path": "/billing-review/{**rest}" } },
  "fallback":       { "ClusterId": "legacy-iis",  "Match": { "Path": "{**catch-all}" }, "Order": 1000 }
}
```

```js
            // Legacy app.routes.js: old bookmarks still arrive as fragments the server never sees.
            .when('/billing/runs/:runId', {
                template: '',
                controller: ['$routeParams', '$window', function ($routeParams, $window) {
                    $window.location.replace('/billing-review/runs/' + $routeParams.runId);
                }]
            })
```

### After: the migrated screen (sketch, current Angular)

```ts
@Component({
  selector: 'fb-run-review',
  imports: [ScrollingModule, CurrencyPipe],
  template: `
    @if (run(); as r) {
      <h2>Run {{ r.Id }} · {{ r.Status }}</h2>
      <button (click)="approve()" [disabled]="r.Status !== 'Complete' || approving()">Approve</button>
    }
    <cdk-virtual-scroll-viewport itemSize="32" class="invoice-list">
      <div *cdkVirtualFor="let invoice of invoices(); trackBy: byId" class="invoice-row">
        {{ invoice.AccountNumber }} {{ invoice.Amount | currency: 'CAD' }}
      </div>
    </cdk-virtual-scroll-viewport>
  `,
})
export class RunReviewComponent implements OnInit {
  private readonly client = inject(BillingRunsClient);   // generated from Billing.Api's OpenAPI document
  private readonly destroyRef = inject(DestroyRef);

  readonly runId = input.required<number>();              // bound from the route with withComponentInputBinding()
  readonly run = signal<BillingRunDto | null>(null);
  readonly invoices = signal<InvoiceRowDto[]>([]);
  readonly approving = signal(false);

  ngOnInit(): void {
    timer(0, 5000).pipe(
      switchMap(() => this.client.getRun(this.runId())),
      tap(run => this.run.set(run)),
      takeWhile(run => run.Status === 'Pending' || run.Status === 'Running', true),
      takeUntilDestroyed(this.destroyRef),                  // FB-262: polling stops when the component goes away
    ).subscribe();
  }

  byId = (_: number, invoice: InvoiceRowDto) => invoice.Id;

  approve(): void { /* POST with the XSRF header added by HttpClient; see video 12 for idempotency */ }
}
```

Property casing in the template (`r.Status`) matches whatever Billing.Api serializes. Make the OpenAPI document and the serializer agree, and prove it with a test (video 11). A generated client built from a camelCase schema, against a PascalCase API, compiles cleanly and renders blanks.

### After: XSRF with a cookie-authenticated SPA (sketch)

```ts
// app.config.ts: the defaults shown explicitly
provideHttpClient(withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }));
```

```csharp
// Gateway or API: accept the header, and issue a readable cookie when the SPA loads.
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");

app.MapGet("/billing-review/xsrf", (IAntiforgery antiforgery, HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!,
        new CookieOptions { HttpOnly = false, Secure = true, SameSite = SameSiteMode.Strict });
    return Results.NoContent();
});
```

JSON endpoints then need explicit validation: `IAntiforgery.ValidateRequestAsync` in an endpoint filter on mutating routes. The built-in antiforgery middleware only validates some endpoint types automatically; verify the behaviour for your ASP.NET Core version before relying on it.

## Demo

```bash
# The legacy UI's assumptions, in four greps
git grep -n "hashPrefix\|#!/" -- legacy/FeeBilling.Web
git grep -n "baseUrl\|FB-279" -- legacy/FeeBilling.Web/App/services/api.service.js
git grep -n "\.Table" -- legacy/FeeBilling.Web/App
git grep -n "FB-247\|FB-262" -- legacy/FeeBilling.Web/App

# Show that the fragment never reaches the server: run the gateway (YARP logs at Information),
# open http://localhost:5000/#!/billing, and point out that both the gateway log and the
# browser's Network tab show a request for "/" only.
dotnet run --project src/FeeBilling.Gateway
```

## Traps to call out

- **Planning to route `#!/billing` at the proxy.** Fragments never leave the browser. The split happens on real paths or not at all.
- **Relative API URLs under a sub-path.** `'api/'` from `/billing-review/runs/42` resolves to `/billing-review/runs/api/...`. Use `/api/`, and a `<base href>` that matches the gateway path.
- **Porting FB-247 as "disable the button".** A disabled button is UX. The fix is server-side idempotency (video 12). Do both.
- **`@for` over 50,000 rows.** Built-in control flow doesn't virtualize. Use the CDK viewport with `*cdkVirtualFor`, and page on the server.
- **Tokens in `localStorage`.** Any XSS can read them. The BFF cookie avoids that, but then XSRF protection is mandatory. Angular only attaches the XSRF header to mutating requests with relative URLs, so absolute URLs silently skip it.
- **Casing drift between the OpenAPI document and the JSON.** Generated clients trust the schema, not the wire.
- **Leaving IE11 hacks in the new app.** The `If-Modified-Since: 1997` header trick was for IE11 caching. Solve caching server-side with proper `Cache-Control`; don't copy the hack.
- **Two different-looking apps.** Users experience the boundary as "the site broke" if the navbar and styling change on one click.

## Key terms

ngUpgrade · route-level strangler · hash (fragment) routing vs path routing · `<base href>` · standalone component · signals · built-in control flow · CDK virtual scroll · `takeUntilDestroyed` / `DestroyRef` · OpenAPI client generation · BFF · XSRF / antiforgery · Playwright

## After the video

1. Write the WP-09 ADR (ngUpgrade vs route-level), including the path scheme, the bookmark redirect and the link changes in `Index.cshtml`.
2. List every `#!/` link and route in the legacy app, and mark which ones cross into migrated screens.
3. Script the Playwright journey (start run, progress, review, approve) against the current legacy app *first*, then keep it green as the screen moves.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 6.2 (Frontend row), Section 9 WP-09, Section 13 (AngularJS row)
- Video 09 (YARP routing), video 11 (JSON contracts), video 12 (idempotency and paging), video 20 (BFF and cookies)
- Angular documentation: standalone components, signals, built-in control flow, `HttpClient` XSRF protection, CDK scrolling, and the upgrade guide (ngUpgrade)
- Microsoft Learn: *Prevent Cross-Site Request Forgery (XSRF/CSRF) attacks in ASP.NET Core*, *Generate OpenAPI documents* (`Microsoft.AspNetCore.OpenApi`)
- Playwright documentation
