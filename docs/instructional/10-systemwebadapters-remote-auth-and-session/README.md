# 10 · SystemWebAdapters: Remote Authentication and Session

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work packages:** WP-08 (transition auth), context for WP-03 · **Prerequisites:** 07, 09

**Audio lesson:** [10-systemwebadapters-remote-auth-and-session.mp3](10-systemwebadapters-remote-auth-and-session.mp3) · [Transcript](script.md)

## Why this video exists

During a strangler-fig migration, users still log in to the legacy app, but requests are increasingly served by new ASP.NET Core services. Those services need to know who the user is. FeeBilling solves this with `Microsoft.AspNetCore.SystemWebAdapters` **remote authentication**: on every request, Accounts.Api calls back into the legacy app to resolve the Forms-auth user. It works, and it has real costs: an extra round trip per request, and a hard dependency on legacy uptime (the handover notes that Accounts.Api returns 500 on everything, including `/health`, when IIS is down). Interviewers ask "how do old and new apps share a logged-in user?" to see whether you know the options and their trade-offs, not just the package name.

## Learning objectives

By the end, the viewer can:

- Name the three SystemWebAdapters packages and say which side of the migration each one runs on.
- Wire remote authentication on both sides and trace a request through it.
- Explain the costs of remote authentication (latency, coupling, security surface) and mitigate them.
- Decide whether remote *session* is needed, and what it requires if it is.
- Compare remote authentication with shared-cookie authentication and with moving straight to OIDC.
- Test services that use remote authentication without running the legacy app, and explain what those tests don't cover.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do old and new apps share a logged-in user during a migration? | Three options. (1) Remote authentication: the new app asks the legacy app who the user is. (2) Shared cookie: both apps read the same auth cookie, which needs compatible cookie middleware and a shared key ring. (3) Move login to an external IdP (OIDC) first and have both apps trust it. FeeBilling uses (1) because legacy Forms auth tickets aren't readable by ASP.NET Core cookie auth without changing legacy login. |
| What are the trade-offs of remote authentication? | Zero change to the legacy login, and it works today. But: a round trip to legacy per authenticated request, new services go down with legacy, the remote-app endpoint and its API key become security-sensitive, and it delays the real target (OIDC). It's a transition mechanism with an exit date. |
| How do you share session state between ASP.NET and ASP.NET Core? | Remote session: the Core app fetches and writes back session values through the legacy app, with every key registered with a serializer on both sides. It's slow and locks per request, so first check whether you need session at all. FeeBilling's web code doesn't use `Session` today. |
| How do you test an ASP.NET Core service whose auth depends on another app? | Replace the scheme in `WebApplicationFactory` with a test authentication handler. Then be explicit that this doesn't test the integration, and cover it separately (an environment test with legacy running). |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Web/FeeBilling.Web.csproj` | `Microsoft.AspNetCore.SystemWebAdapters.FrameworkServices` |
| `src/FeeBilling.Accounts.Api/FeeBilling.Accounts.Api.csproj` | `Microsoft.AspNetCore.SystemWebAdapters.CoreServices` |
| `legacy/FeeBilling.Web/Global.asax.cs` | `AddRemoteAppServer(options => options.ApiKey = ...)`, `.AddAuthenticationServer()` |
| `legacy/FeeBilling.Web/Web.config` | `RemoteAppApiKey`, the `SystemWebAdapterModule` registration, `<authentication mode="Forms">`, `<sessionState mode="InProc" />` |
| `src/FeeBilling.Accounts.Api/Program.cs` | `AddRemoteAppClient(...)`, `.AddAuthenticationClient(true)`, middleware order ending in `app.UseSystemWebAdapters()` |
| `src/FeeBilling.Accounts.Api/appsettings.json` | `RemoteApp:Url` and the **same** API key GUID as `Web.config`, both committed |
| `tests/FeeBilling.Accounts.Api.Tests/Infrastructure/AccountsApiFixture.cs` | `ConfigureTestServices` swapping in the test scheme |
| `tests/FeeBilling.Accounts.Api.Tests/Infrastructure/TestAuthHandler.cs` | `X-Test-User` header → `ClaimsPrincipal` |
| `docs/handover.md` | "If IIS is down, Accounts.Api returns 500 on everything, including `/health`." |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Accounts.Api doesn't use legacy for data or health, yet with IIS stopped every request returns 500, including `/health` (per the handover). Why would a health endpoint depend on another app? |
| 01:30–04:30 | The problem and the options | Users log in once, to legacy (Forms auth, custom membership, `machineKey`-protected ticket in `.FEEBILLINGAUTH`). Options: remote auth, shared cookie, OIDC first. Why the previous team picked remote auth: no change to the legacy login page, and ASP.NET Core can't read a Forms auth ticket out of the box. |
| 04:30–08:30 | Wiring both sides | The three packages: `Microsoft.AspNetCore.SystemWebAdapters` (shared `System.Web` API surface for `netstandard2.0` libraries), `.FrameworkServices` (the IIS side: an HTTP module plus remote-app server endpoints), `.CoreServices` (the ASP.NET Core side). Walk `Global.asax.cs`, the `Web.config` module entry, then Accounts.Api `Program.cs`. `AddAuthenticationClient(true)` makes remote auth the **default** authentication scheme. |
| 08:30–11:30 | A request, step by step | Draw the sequence (below). Accounts.Api forwards the caller's cookies to a legacy endpoint with the shared API key; legacy runs its Forms auth pipeline and returns the user; Accounts.Api builds a `ClaimsPrincipal`. Every authenticated request pays this round trip. |
| 11:30–14:00 | The costs | Latency per request. Availability coupling: because remote auth is the default scheme and `UseAuthentication()` runs for every request, the call to legacy happens even on endpoints that allow anonymous access (verify the exact behaviour for your package version). Load on the legacy app grows as *more* traffic moves to new services, the opposite of what a migration should do. |
| 14:00–16:00 | Remote session | What it needs: `AddSessionClient()` / `AddSessionServer()`, a JSON session serializer with every key registered by type on both sides, and endpoints opted in to session. Per-request locking. Then the FeeBilling answer: `git grep` finds no `Session[` usage, so don't add remote session "just in case". |
| 16:00–18:00 | Security and mitigations | The API key is committed in two files; rotate it and move it to a secret store (video 19). The gateway's catch-all forwards *every* path to IIS, so the remote-app endpoints are reachable through the public front door unless the gateway blocks them. Accounts.Api should reach legacy over an internal address (it does: `RemoteApp:Url`). Keep `/health` and `/alive` out of the remote-auth path (make remote auth a non-default scheme applied to the API groups, or branch the pipeline), add a timeout on the remote call, and alert on its latency. |
| 18:00–19:15 | Testing | `AccountsApiFixture` replaces remote auth with `TestAuthHandler` via `PostConfigure<AuthenticationOptions>`. `ListAccounts_WithoutUser_Returns401` proves authorization is enforced. What's untested: the real handshake with legacy. Cover it with one environment-level test on Windows. |
| 19:15–20:00 | Recap and exit plan | Remote auth is a bridge. The exit is OIDC at the gateway with the legacy app accepting the same identity (video 20), after which the SystemWebAdapters remote-app server is removed. |

### Before: both halves of the bridge

```csharp
// legacy/FeeBilling.Web/Global.asax.cs
SystemWebAdapterConfiguration.AddSystemWebAdapters(this)
    .AddProxySupport(options => options.UseForwardedHeaders = true)
    .AddRemoteAppServer(options => options.ApiKey = ConfigurationManager.AppSettings["RemoteAppApiKey"])
    .AddAuthenticationServer();
```

```csharp
// src/FeeBilling.Accounts.Api/Program.cs
builder.Services.AddSystemWebAdapters()
    .AddRemoteAppClient(options =>
    {
        options.RemoteAppUrl = new Uri(builder.Configuration["RemoteApp:Url"]
            ?? throw new InvalidOperationException("RemoteApp:Url is not configured."));
        options.ApiKey = builder.Configuration["RemoteApp:ApiKey"]
            ?? throw new InvalidOperationException("RemoteApp:ApiKey is not configured.");
    })
    .AddAuthenticationClient(true);
```

### The request flow

```
Browser ──cookie .FEEBILLINGAUTH──▶ Gateway (YARP) ──▶ Accounts.Api
                                                          │  UseAuthentication(): remote scheme is the default
                                                          │  forwards cookie + API key
                                                          ▼
                                                   legacy IIS (FeeBilling.Web)
                                                   SystemWebAdapterModule → Forms auth → user + roles
                                                          │
                                                          ▼
                                        Accounts.Api builds ClaimsPrincipal → endpoint runs
```

### After: keep health checks independent of legacy (sketch)

```csharp
// Remote auth no longer the default scheme; API groups opt in explicitly.
builder.Services.AddSystemWebAdapters()
    .AddRemoteAppClient(options => { /* as before, key from a secret store */ })
    .AddAuthenticationClient(false);

var group = app.MapGroup("/api/accounts")
    .RequireAuthorization(new AuthorizeAttribute
    {
        AuthenticationSchemes = RemoteAppAuthenticationDefaults.AuthenticationScheme,
    });
```

`/health` and `/alive` stay anonymous and no longer trigger a call to legacy. Check the scheme-name constant against the package version you use before recording.

## Demo

```bash
docker compose up -d
dotnet run --project src/FeeBilling.Accounts.Api        # legacy IIS NOT running

curl -i http://localhost:5101/health                    # handover: 500 while IIS is down
curl -i "http://localhost:5101/api/accounts?firmId=1"   # 500: the remote call to http://localhost:8080 fails

# Does the legacy web app use session at all?
git grep -n "Session\[" -- legacy

# The shared secret, committed twice
git grep -n "0c2f3f5e-6a44-4f7e-9b0c-2d1f6a1e9b7a"

# The tests don't need legacy, because they swap the scheme
dotnet test tests/FeeBilling.Accounts.Api.Tests
```

If a Windows machine with IIS Express is available, repeat the first two `curl` calls with the legacy app running and show the extra request in the legacy log for every call to Accounts.Api.

## Traps to call out

- **Treating remote auth as the destination.** It adds load to the legacy app as migration progresses. Put the exit (OIDC) on the plan with a date.
- **A health check that depends on another system.** A liveness probe that fails when legacy is down makes an orchestrator restart healthy pods in a loop. Keep liveness local; make readiness deliberate about dependencies.
- **Assuming Forms auth cookies can be shared directly.** ASP.NET Core cookie auth can share cookies with ASP.NET 4.x apps that use OWIN cookie authentication and a shared Data Protection key ring. Forms auth tickets are a different format (verify the current interop guidance before recommending a route).
- **Committed secrets.** The remote-app API key sits in `Web.config` and `appsettings.json`. Anyone with repo access can call the legacy authentication endpoint.
- **Exposing the remote-app endpoints through the gateway.** The catch-all route forwards them to the internet unless you block the path.
- **Adding remote session by default.** It's slow, it serializes every value, and FeeBilling doesn't need it.
- **Believing green integration tests prove auth works.** They prove authorization rules with a *fake* identity. The handshake is untested until an environment test covers it.

## Key terms

Remote authentication · remote session · SystemWebAdapters (FrameworkServices / CoreServices) · authentication scheme · default scheme · API key · Forms authentication ticket · Data Protection key ring · test authentication handler

## After the video

1. Write the ADR for the auth transition: remote auth today, what triggers the move to OIDC, and the date the remote-app server is removed from `Global.asax.cs`.
2. Change the Accounts.Api design (on paper) so `/health` and `/alive` never call legacy, and list the tests that would prove it.
3. List every place the remote-app API key must be rotated, and where it should live instead.

## References

- `docs/handover.md` ("Auth during the transition"), `docs/adr/0001-strangler-fig-migration.md`
- `docs/brasswick-modernization-training-plan.md`: Section 2 (incremental ASP.NET migration), Section 7 (auth during transition), WP-08
- Microsoft Learn: *Incremental ASP.NET to ASP.NET Core migration*, *Remote app setup*, *Remote authentication*, *Remote app session state* (System.Web adapters)
- Microsoft Learn: *Share authentication cookies among ASP.NET apps*
