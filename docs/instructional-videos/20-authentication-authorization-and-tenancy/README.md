# 20 · Authentication, Authorization and Tenancy

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-08 · **Prerequisites:** 07, 10, 13

## Why this video exists

Enterprise clients want to sign in with their own identity provider, and every one of them will ask how you stop Firm A seeing Firm B's invoices. FeeBilling's legacy security is Forms authentication, a custom membership provider with SHA1 password hashes, and roles cached in a cookie. The part that matters most for a multi-tenant billing system is also missing: **nothing checks which firm the caller belongs to.** This video moves FeeBilling to OIDC without logging everyone out, replaces roles with policies, and enforces tenant isolation in a way a test can prove.

## Learning objectives

By the end, the viewer can:

- Inventory a Forms-auth application's security surface: cookie, `machineKey`, membership and role providers, password storage, lockout and anti-forgery.
- Design OIDC for a multi-tenant vendor: Entra ID with per-client IdP federation, the gateway as a backend-for-frontend (BFF) holding the cookie, and APIs validating access tokens.
- Plan the transition so users sign in once, and replace `machineKey` with a shared ASP.NET Core Data Protection key ring.
- Map legacy roles to named authorization policies, and explain why policies outlive roles.
- Enforce tenant isolation with a `FirmId` claim and EF Core global query filters, name the ways around them, and write a test that proves no endpoint leaks another firm's data.
- Add an audit trail for fee schedule changes, with before and after values.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do you migrate from Forms auth to OIDC without logging everyone out? | Map existing users to IdP identities first. Run both sign-in paths for a window, switched per firm by a flag. Make legacy trust the new identity (SSO against the same IdP, or a shared cookie) so users sign in once. Let old Forms cookies expire naturally (120-minute sliding timeout here). Never force a password reset as the migration mechanism. |
| How do you enforce tenant isolation? | Tenant comes from a trusted claim, never from the query string. Enforce it in the data layer (global query filters) *and* test it at the HTTP layer across every endpoint. Treat `IgnoreQueryFilters()` and raw SQL as reviewed exceptions. Model cross-tenant staff explicitly instead of as "no filter". |
| Roles vs policies? | A role is a fact about a user. A policy is a rule for an action (`CanStartBillingRun`). Endpoints reference policies, so changing who may do something is one change in one place, and policies can combine roles, claims and resource checks (the firm in the request). |
| Where should tokens live in a browser app? | Not anywhere JavaScript can read them. The BFF holds the tokens (in its encrypted ticket or a server-side session store) and gives the SPA an `HttpOnly`, `Secure`, `SameSite` cookie. That shrinks the XSS blast radius, but it brings back CSRF, so you need antiforgery. |
| What would you fix first in this legacy auth? | The missing tenant check (any user can read any firm's accounts), then the missing role check on starting a billing run. Password hashing matters less if passwords move to the IdP. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Web/Web.config` | `<forms ... name=".FEEBILLINGAUTH" timeout="120" slidingExpiration="true" />`, the committed `machineKey`, `roleManager cacheRolesInCookie="true"` |
| `legacy/FeeBilling.Web/Web.ClientX.config` | A second, per-client `machineKey`, also committed |
| `legacy/FeeBilling.Web/Security/FeeBillingMembershipProvider.cs` | `MaxInvalidPasswordAttempts => int.MaxValue` (no lockout) |
| `legacy/FeeBilling.Web/Security/PasswordHasher.cs` | `SHA1(salt + password)`, compared with `==` (not constant-time) |
| `legacy/FeeBilling.Web/App_Start/WebApiConfig.cs` | Global `AuthorizeAttribute`: authenticated, but no role or firm check |
| `legacy/FeeBilling.Web/Controllers/Api/BillingController.cs` | `StartRun` has **no** role check; `ApproveRun` has `[Authorize(Roles = "BillingAdmin")]` |
| `legacy/FeeBilling.Web/Controllers/Api/FeeSchedulesController.cs` | `[Authorize(Roles = "ScheduleEditor")]`, `// No audit trail beyond ModifiedBy (FB-340).` |
| `legacy/FeeBilling.Web/App/services/api.service.js` | `// TODO FB-279: POSTs don't send the anti-forgery token.` |
| `src/FeeBilling.Accounts.Api/Accounts/AccountsEndpoints.cs` | `ListAccounts(int firmId, ...)`: firm taken from the query string, not from the caller |
| `database/billing/001-schema.sql` | `AppUsers.FirmId ... NULL = vendor staff (sees all firms)` |
| `database/seed/010-seed-q3-2026.sql` | `maple.ops` is firm 1, role `BillingReviewer` only |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Two facts from the code. `maple.ops`, a reviewer at Maple Ridge, can start a billing run for *any* firm, because `StartRun` only checks "authenticated". The same user can list Groupe Financier Laurentien's 500 accounts through the *already migrated* Accounts.Api by changing `firmId=1` to `firmId=2`. |
| 01:30–05:00 | Legacy security inventory | Forms cookie, `machineKey` committed in two files (rotate them, and the database passwords next to them). Custom membership over ADO.NET, SHA1 with a salt but no iterations, `==` comparison, no lockout. Roles cached in a cookie, so revoking a role waits for the cookie to expire. Global `AuthorizeAttribute`. API POSTs carry no anti-forgery token. |
| 05:00–08:30 | Target architecture | OIDC with Entra ID, designed so each enterprise client can federate its own IdP. The gateway is the BFF: it runs the OIDC code flow, keeps tokens out of reach of JavaScript, and gives the browser a cookie. YARP attaches the access token when forwarding to APIs, which validate it with JWT bearer. Discuss the alternative, APIs trusting identity headers from the gateway: simpler, but only safe if the APIs are unreachable except through the gateway. |
| 08:30–11:00 | Transition: sign in once | Map `AppUsers` to IdP identities (`Email` is nullable, so this needs a reconciliation step). Switch sign-in per firm with a flag. Legacy must accept the new identity (video 10): either legacy becomes an OIDC client of the same IdP (an SSO redirect, no second password), or both apps read one shared cookie (OWIN cookie auth in legacy plus a shared Data Protection key ring; verify the interop packages before recording). `machineKey` becomes a Data Protection key ring that is persisted, encrypted and shared. |
| 11:00–13:30 | Roles to policies | Build the mapping table below on screen. Set a fallback policy so nothing is accidentally anonymous. The Entra `roles` claim trap (see below). Resource-based checks: "may this user start a run *for this firm*?" is a policy plus the resource, not a role. |
| 13:30–17:30 | Tenant isolation | Live IDOR demo against Accounts.Api. Fix it in layers: an `ITenantContext` from the `firm_id` claim, endpoints that stop accepting `firmId` from callers who have one, and EF Core query filters as the backstop. Vendor staff (`FirmId IS NULL`) is an explicit, audited mode, not "no filter". Ways around the filter: `IgnoreQueryFilters()`, `FromSql`, stored procedures, Dapper, DbContext pooling with a cached tenant. The cross-tenant test that walks every endpoint. |
| 17:30–19:00 | Audit log | FB-340. Capture who (immutable subject ID, not display name), when, which schedule, and before and after tier values. Two options: a `SaveChangesInterceptor` writing an append-only audit table, or SQL Server temporal tables (EF Core supports `IsTemporal()`). Mid-quarter schedule edits change open invoices, so the audit feeds video 23's reconciliation. |
| 19:00–20:00 | Recap | Tenant from claims, enforced in data and proven over HTTP. Policies, not roles, on endpoints. Tokens never in the browser. Sign in once during the transition. |

### Legacy roles to policies

| Policy | Legacy equivalent | Requirement |
|---|---|---|
| `CanViewBilling` | global `[Authorize]` | Role `BillingReviewer` or `BillingAdmin`, and a firm claim or vendor-staff flag |
| `CanStartBillingRun` | *none*: any authenticated user today | Role `BillingAdmin`, plus the run's firm matching the caller's firm |
| `CanApproveInvoices` | `[Authorize(Roles = "BillingAdmin")]` on `ApproveRun` | Role `BillingAdmin` (consider a four-eyes rule: the starter can't approve) |
| `CanEditFeeSchedules` | `[Authorize(Roles = "ScheduleEditor")]` | Role `ScheduleEditor`, plus the schedule's firm matching (platform templates have `FirmId IS NULL`) |

### Before: authenticated means authorized

```csharp
        public override int MaxInvalidPasswordAttempts { get { return int.MaxValue; } }   // no lockout
```

```csharp
        public static bool Verify(string salt, string password, string expectedHash)
        {
            return Hash(salt, password) == expectedHash;   // not constant-time
        }
```

```csharp
    private static async Task<Ok<List<AccountDto>>> ListAccounts(
        int firmId,
        string? search,
        FeeBillingDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.Accounts
            .AsNoTracking()
            .Include(a => a.Household)
            .Where(a => a.FirmId == firmId);
```

### After: gateway as BFF (sketch)

```csharp
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-feebilling";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddOpenIdConnect(options =>
    {
        builder.Configuration.Bind("Oidc", options);   // Authority, ClientId, client credentials
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = true;                      // in the encrypted HttpOnly ticket; JavaScript can't read them
        options.MapInboundClaims = false;
    });

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context => context.AddRequestTransform(async transform =>
    {
        var accessToken = await transform.HttpContext.GetTokenAsync("access_token");
        if (accessToken is not null)
        {
            transform.ProxyRequest.Headers.Authorization = new("Bearer", accessToken);
        }
    }));
```

Two things are not shown. First, token refresh: a real BFF has to refresh expired access tokens, or sessions break silently after about an hour. Microsoft.Identity.Web or a BFF library handles this; choose one deliberately. Second, cookie size: saved tokens make the cookie large (it gets chunked). A server-side ticket store (`CookieAuthenticationOptions.SessionStore`) keeps only a session ID in the browser.

### After: API validation and policies (sketch)

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Oidc:Authority"];
        options.Audience = builder.Configuration["Oidc:Audience"];
        options.MapInboundClaims = false;
        options.TokenValidationParameters.RoleClaimType = "roles";   // Entra app roles; RequireRole silently fails without this
        options.TokenValidationParameters.NameClaimType = "name";
    });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("CanStartBillingRun", policy => policy.RequireRole("BillingAdmin"))
    .AddPolicy("CanApproveInvoices", policy => policy.RequireRole("BillingAdmin"))
    .AddPolicy("CanEditFeeSchedules", policy => policy.RequireRole("ScheduleEditor"));

billing.MapPost("/runs", StartBillingRun).RequireAuthorization("CanStartBillingRun");
```

### After: `machineKey` becomes a shared Data Protection key ring (sketch)

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("FeeBilling")   // same value in every app that must read the cookie
    .PersistKeysToAzureBlobStorage(new Uri(builder.Configuration["DataProtection:BlobUri"]!), new DefaultAzureCredential())
    .ProtectKeysWithAzureKeyVault(new Uri(builder.Configuration["DataProtection:KeyId"]!), new DefaultAzureCredential());
```

Without a persisted key ring, every container restart or scale-out invalidates every cookie. That's the containerized version of mismatched `machineKey`s across a web farm.

### After: tenant filter in the data layer (sketch)

```csharp
public class FeeBillingDbContext(DbContextOptions<FeeBillingDbContext> options, ITenantContext tenant) : DbContext(options)
{
    private readonly int? _firmId = tenant.FirmId;               // null only for vendor staff
    private readonly bool _isVendorStaff = tenant.IsVendorStaff;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // EF Core 10 named filters: tenant and soft-delete can be ignored independently.
        // Verify the exact overloads before recording.
        modelBuilder.Entity<Account>()
            .HasQueryFilter("Tenant", a => _isVendorStaff || a.FirmId == _firmId);
    }
}
```

EF Core evaluates filters that reference context fields *per context instance*, which is why the tenant is captured in fields. Before EF Core 10, an entity could have only one filter, so combining tenant and soft-delete meant `IgnoreQueryFilters()` removed both.

### After: the cross-tenant test (sketch)

```csharp
[Fact]
public async Task NoGetEndpoint_ReturnsAnotherFirmsData()
{
    var client = fixture.CreateAuthenticatedClient("maple.ops");   // firm 1
    var endpoints = fixture.Factory.Services.GetRequiredService<EndpointDataSource>()
        .Endpoints.OfType<RouteEndpoint>()
        .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);

    foreach (var endpoint in endpoints)
    {
        var url = FirmTwoUrlFor(endpoint.RoutePattern);   // fill {id} with a firm 2 id, add ?firmId=2
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"{endpoint.DisplayName} returned {(int)response.StatusCode} for another firm's data");
    }
}
```

Because the test *discovers* endpoints, a new endpoint added next month is covered automatically. That's the property you want.

## Demo

```bash
# Legacy: what does authorization actually check?
git grep -n "Authorize" -- legacy/FeeBilling.Web
git grep -n "machineKey\|RemoteAppApiKey" -- legacy

# Modern: the IDOR, live. Add this test to tests/FeeBilling.Accounts.Api.Tests. It fails today, which proves the IDOR:
#   var accounts = await fixture.CreateAuthenticatedClient("maple.ops")
#       .GetFromJsonAsync<List<AccountDto>>("/api/accounts?firmId=2", ...);
#   Assert.Empty(accounts);   // today: 500 Groupe Financier Laurentien accounts
dotnet test tests/FeeBilling.Accounts.Api.Tests --filter "FullyQualifiedName~Accounts"
```

`TestAuthHandler` only issues a `Name` claim, so part of the fix is teaching the test scheme to issue `firm_id` and `roles` claims. That way tests exercise the same policies production uses.

## Traps to call out

- **Tenant from the request.** `firmId` in a query string or body is a filter the caller controls, not an authorization decision. Accounts.Api already shipped this.
- **Authenticated is not authorized.** Legacy `StartRun` relied on the global `AuthorizeAttribute`. Moving it as-is ports the hole.
- **`MapInboundClaims` and role claim types.** With inbound mapping off, Entra sends app roles as `roles`. Unless `RoleClaimType` is set, `RequireRole` never matches and every call returns 403. The opposite mistake, leaving mapping on, gives you long `http://schemas...` claim types in some places and short names in others.
- **"No filter" for vendor staff.** `FirmId IS NULL` users are real. Model them as an explicit, logged mode, or a support engineer's query silently becomes cross-tenant.
- **Filter bypasses.** `IgnoreQueryFilters()`, `FromSql`/`SqlQuery`, stored procedures (`usp_GetBillableAum` knows nothing about tenants) and DbContext pooling with a tenant captured at construction all skip or break the filter. Add an architecture test or analyzer, and require review for each exception.
- **Cookie auth brings back CSRF.** Moving tokens out of the browser is right, but FB-279 (no anti-forgery on POSTs) becomes critical. Use antiforgery plus `SameSite` (video 22).
- **Roles in cookies.** `cacheRolesInCookie` means a revoked role survives until the cookie expires. Access tokens have the same property, bounded by their lifetime. Say how long revocation takes.
- **Forced password resets as a migration plan.** If local passwords survive at all (for example, vendor staff), verify the SHA1 hash once with `CryptographicOperations.FixedTimeEquals`, then rehash with `PasswordHasher<TUser>` (PBKDF2) on that successful login. Better still, move them to the IdP.
- **Audit by display name.** Names change; the subject ID (`sub`/`oid`) doesn't.

## Key terms

OIDC authorization code flow · BFF (backend-for-frontend) · access token vs ID token · audience · app roles · policy-based authorization · resource-based authorization · fallback policy · Data Protection key ring · IDOR (insecure direct object reference) · global query filter · named query filter · four-eyes approval · temporal table

## After the video

1. Write the failing cross-tenant test for Accounts.Api (`maple.ops` reading `firmId=2`), then fix the endpoint so the firm comes from the caller's claim.
2. Draft the role-to-policy mapping as an ADR, including how vendor staff are handled and who approves exceptions to tenant filtering.
3. Write the transition sequence as a runbook section: identity mapping, per-firm switch, how legacy trusts the new identity, and when Forms auth is switched off.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 6.2 (Auth row), Section 9 WP-08, Section 13 (Forms auth, `machineKey` rows)
- `docs/handover.md`: "Auth during the transition"
- Video 10 (SystemWebAdapters remote authentication), video 22 (SPA, BFF cookie and XSRF)
- Microsoft Learn: *Policy-based authorization in ASP.NET Core*, *Configure ASP.NET Core Data Protection*, *Share authentication cookies among ASP.NET apps*, *Global Query Filters* (EF Core)
- Microsoft identity platform: *App roles* and *Microsoft.Identity.Web*
- OWASP: *Insecure Direct Object Reference Prevention Cheat Sheet*, *Password Storage Cheat Sheet*
