# 10 · SystemWebAdapters: Remote Authentication and Session

Welcome. During a strangler-fig migration, users still log in to the legacy application, but more and more of their requests are served by new ASP.NET Core services. Those services need to know who the user is. FeeBilling solves that with a feature of the `Microsoft.AspNetCore.SystemWebAdapters` packages called remote authentication. It works, and it has real costs. By the end of this lesson, you'll be able to explain the options for sharing a logged-in user between old and new apps, wire and trace remote authentication, name its costs and mitigations, decide whether you need remote session, and test a service whose authentication depends on another application.

## The questions this lesson answers

Here are the questions this lesson prepares you for. How do old and new apps share a logged-in user during a migration? What are the trade-offs of remote authentication? How do you share session state between ASP.NET and ASP.NET Core? And how do you test an ASP.NET Core service whose authentication depends on another app?

Interviewers ask the first one to see whether you know the options and their trade-offs, not just the name of a package. So we'll start with the options.

## The problem, and three ways to solve it

In FeeBilling, login belongs to the legacy app. Open `legacy/FeeBilling.Web/Web.config`. The authentication mode is Forms. The login URL is the account login page, and the cookie is named FeeBilling auth, written in capitals with a leading dot, with a two-hour sliding timeout. Users are validated by a custom membership provider over the app's own user tables, and the ticket inside the cookie is encrypted and signed with the machine key that's also in that file. So the identity of a logged-in user lives in a Forms authentication ticket that only ASP.NET on .NET Framework knows how to read.

The machine key deserves a moment. The comment above it in `Web.config` says it's shared across the web farm, and now also by the SystemWebAdapters remote-auth endpoint. Every IIS node must have the same key, or a cookie issued by one node can't be read by another. And the dedicated ClientX instance has its own key in `Web.ClientX.config`. Whatever replaces Forms authentication has to handle key sharing just as deliberately, which in ASP.NET Core means a shared Data Protection key ring.

There are three broad ways to let a new service see that user.

Option one is remote authentication. The new app doesn't try to read the cookie itself. On each request, it asks the legacy app, "who is this?", by forwarding the request's cookies to a special endpoint on the legacy side. The legacy app runs its normal Forms authentication pipeline and answers with the user.

Option two is a shared cookie. Both apps read the same authentication cookie. That needs compatible cookie middleware on both sides and a shared key ring. ASP.NET Core cookie authentication can share cookies with ASP.NET 4 applications that use OWIN cookie authentication and a shared Data Protection key ring. A Forms authentication ticket is a different format, so this route would mean changing how the legacy app issues its login cookie first. Check the current interop guidance before you recommend it.

Option three is to move login to an external identity provider first, using OpenID Connect, and have both the old and new apps trust it. That's the real target, and lesson twenty covers it. But it's the biggest change of the three.

The previous team picked option one, and it was a reasonable choice. It needs no change to the legacy login page, and it works with Forms authentication exactly as it is today.

## Wiring both sides

The adapters come as three packages, and you should know which side each one runs on. `Microsoft.AspNetCore.SystemWebAdapters` is the shared one: it provides a `System.Web` API surface that `netstandard2.0` libraries can compile against. `Microsoft.AspNetCore.SystemWebAdapters.FrameworkServices` runs on the .NET Framework side, inside IIS. `Microsoft.AspNetCore.SystemWebAdapters.CoreServices` runs on the ASP.NET Core side. In this repo, `legacy/FeeBilling.Web/FeeBilling.Web.csproj` references FrameworkServices, and `src/FeeBilling.Accounts.Api/FeeBilling.Accounts.Api.csproj` references CoreServices.

On the legacy side, open `legacy/FeeBilling.Web/Global.asax.cs`. In `Application_Start`, after the usual MVC and Web API registration, there's a block with a comment explaining that the ASP.NET Core services behind the gateway call back into this app to resolve the Forms-auth user, and that every request to Accounts.Api costs a round trip here. The code calls `AddSystemWebAdapters`, then `AddProxySupport` with forwarded headers turned on, then `AddRemoteAppServer`, setting the API key from the `RemoteAppApiKey` app setting, and finally `AddAuthenticationServer`. `Web.config` also registers the `SystemWebAdapterModule` as an HTTP module, which is how the adapters see requests in IIS.

On the new side, open `src/FeeBilling.Accounts.Api/Program.cs`. There's a comment that says it plainly: login is still owned by the legacy app, each request resolves the user by calling back into legacy, so this API is down whenever legacy is. The code calls `AddSystemWebAdapters`, then `AddRemoteAppClient`, which takes two settings: the legacy app's URL, from `RemoteApp:Url`, and the same API key, from `RemoteApp:ApiKey`. Both throw at startup if they're missing, which is good practice. Then it calls `AddAuthenticationClient` with the argument true. That true matters: it makes remote authentication the default authentication scheme for the whole app.

Further down, the middleware order is exception handler, then `UseAuthentication`, then `UseAuthorization`, then `UseSystemWebAdapters`, and then the endpoints are mapped.

## A request, step by step

Let's trace one request. A browser that has logged in to legacy sends a request for the accounts list, carrying the Forms authentication cookie. It hits the gateway, and YARP's accounts route forwards it to Accounts.Api.

In Accounts.Api, `UseAuthentication` runs the default scheme, which is remote authentication. The remote client makes an HTTP call to the legacy app at the configured URL, forwarding the caller's cookies along with the shared API key. On the legacy side, the adapters' module recognizes the call, checks the key, and runs the normal Forms authentication pipeline on the forwarded cookie: decrypt the ticket, check it's valid, and load the user and roles. It sends the result back. Accounts.Api turns that into a `ClaimsPrincipal`, authorization runs against it, and finally the endpoint executes.

So every authenticated request to the new service costs one extra round trip to the old service. That's the price of not changing the legacy login.

It's tempting to cache the answer: remember, for a minute or two, which user a given cookie belongs to, and skip the call. That cuts the round trips, but it has a cost of its own. If a user logs out, or an administrator locks an account, the new service keeps trusting the cached identity until the entry expires. For a billing system, where the roles decide who can approve invoices, decide that window deliberately and write it down, rather than discovering it later. And check what the adapters do for you before building your own cache.

## The costs

Let's be precise about the costs, because naming them is what makes your answer credible.

Latency. Every request pays for a second HTTP call before any real work starts. It adds up quickly: a screen that makes five API calls makes five authentication calls to legacy, and the slowest of them sets the pace.

Availability coupling. If the legacy app is down, the new service can't authenticate anyone. The handover in `docs/handover.md` goes further: if IIS is down, Accounts.Api returns a 500 on everything, including its health endpoint. Why would a health endpoint depend on another application? The likely explanation is that remote authentication is the default scheme, and `UseAuthentication` runs for every request, so the call to legacy happens even for endpoints that allow anonymous access. Verify the exact behaviour for your package version, but the lesson holds either way: a health check that depends on another system is a bad health check. If an orchestrator restarts pods whenever their liveness probe fails, a legacy outage would make it restart perfectly healthy pods in a loop.

Load moves the wrong way. As more traffic migrates to new services, more requests call back into legacy for authentication. A migration should shrink the legacy app's load, and this grows it.

And the security surface grows. The remote-app endpoint on the legacy side, and the API key that protects it, are now security-sensitive.

## Remote session

The adapters can also share session state. With remote session, the ASP.NET Core app fetches the user's session values from the legacy app at the start of a request and writes changes back at the end. It needs `AddSessionClient` on the Core side and `AddSessionServer` on the Framework side. Every session key has to be registered with its type, so both sides can serialize it the same way. Endpoints opt in to session, and a session is locked for the duration of a request.

That's slow, and it's fiddly. So the first question is always: do you need session at all? For FeeBilling, the answer comes from one command. A `git grep` for session indexer usage across the legacy folder finds nothing. The web app's `Web.config` does set session state to in-process, but the code doesn't read or write it. So don't add remote session just in case. The cheapest distributed session is the one you don't have.

## Security, mitigations and testing

Start with the secret. The API key is a GUID, and it's committed twice: in `legacy/FeeBilling.Web/Web.config` as `RemoteAppApiKey`, and in `src/FeeBilling.Accounts.Api/appsettings.json` under `RemoteApp:ApiKey`. Anyone with read access to the repository can call the legacy authentication endpoint. Rotate it, and move it into a secret store, which lesson nineteen covers.

Next, the front door. The gateway's catch-all route forwards every path to IIS. That includes the remote-app endpoints the adapters add to the legacy app. Unless the gateway explicitly blocks those paths, they're reachable from the internet. Accounts.Api should talk to legacy over an internal address, and it does: `RemoteApp:Url` points straight at the legacy app, not at the gateway. But the public path needs closing too.

Then make health independent. One approach: pass false to `AddAuthenticationClient`, so remote authentication is no longer the default scheme, and have the API endpoint groups require authorization with the remote scheme explicitly. The health and liveness endpoints stay anonymous and never trigger a call to legacy. Add a timeout on the remote call, and alert on its latency, because it's now on the critical path of every request.

Finally, testing. Open `tests/FeeBilling.Accounts.Api.Tests/Infrastructure/AccountsApiFixture.cs`. It sets the remote app URL to a deliberately invalid address, legacy dot invalid, and a placeholder key. Then, in `ConfigureTestServices`, it adds a test authentication scheme and uses `PostConfigure` on the authentication options to make the test scheme the default for authenticate and challenge. The handler in `TestAuthHandler.cs` authenticates any request that carries an `X-Test-User` header, building a claims identity with that name. And there's a test, `ListAccounts_WithoutUser_Returns401`, that proves authorization is actually enforced when there's no user.

Why `PostConfigure`, rather than a plain configure call? Because the application has already set its own default scheme during startup, when `AddAuthenticationClient` was passed true. Configure callbacks run in registration order, so a test's plain configure could lose to, or be overwritten by, the app's own settings. Post-configure callbacks run after every configure callback, so the test scheme reliably wins. That's a small detail, and it's exactly the kind that makes tests pass on one machine and fail on another.

Be honest about what that doesn't cover. These tests prove the authorization rules with a fake identity. They say nothing about the real handshake with legacy. Cover that with at least one environment-level test, on Windows, with the legacy app running.

## The exit plan

Remote authentication is a bridge, so plan the far side of it before you rely on it. The exit looks like this. First, introduce an OpenID Connect identity provider and let the gateway own the login and the session cookie. Second, make the legacy app accept that same identity, so a user who signs in once is known to both old and new code. Third, switch the new services from the remote scheme to the new identity, one service at a time, each behind its own route. Fourth, once no service calls back into legacy for authentication, remove `AddRemoteAppServer` and `AddAuthenticationServer` from `Global.asax.cs`, remove the module registration, delete the API key from every configuration file, and remove the adapters' client registration from the new services.

Each of those steps is independently deployable, which is the same principle as the rest of the migration. Write the exit into an ADR with a trigger and a date, so the bridge doesn't quietly become permanent.

## Traps

The traps to call out.

Treating remote authentication as the destination. It's a bridge with an exit: OpenID Connect at the gateway, with the legacy app accepting the same identity. Put a date on the exit.

A health check that depends on another system. Keep liveness local, and make readiness deliberate about its dependencies.

Assuming Forms authentication cookies can be shared directly with ASP.NET Core. They're a different format from what ASP.NET Core cookie authentication understands.

Committed secrets. The same key in two files.

Exposing the remote-app endpoints through the gateway's catch-all.

Adding remote session by default. It's slow, and FeeBilling doesn't use session.

And believing green integration tests prove authentication works. They prove authorization with a fake identity.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do old and new apps share a logged-in user during a migration?

[pause 5s]

There are three options. Remote authentication, where the new app asks the legacy app who the user is on each request. A shared cookie, where both apps read the same authentication cookie, which needs compatible cookie middleware and a shared key ring. Or moving login to an external identity provider with OpenID Connect first, and having both apps trust it. FeeBilling uses remote authentication through SystemWebAdapters, because the legacy app uses Forms authentication, and ASP.NET Core can't read a Forms ticket without changing the legacy login. The long-term target is OpenID Connect.

**Interviewer:** What are the trade-offs of remote authentication?

[pause 5s]

The upside is zero change to the legacy login, and it works today. The downsides: an extra round trip to legacy on every authenticated request; new services go down whenever legacy is down, and in FeeBilling even the health endpoint returns 500; the load on legacy grows as more traffic migrates, which is backwards; and the remote-app endpoint and its API key become security-sensitive. So it's a transition mechanism with an exit date, not a design.

**Interviewer:** How do you share session state between ASP.NET and ASP.NET Core?

[pause 5s]

With the adapters' remote session feature: the Core app fetches session values from the legacy app and writes them back, with every key registered with a serializer on both sides, and the session locked per request. It's slow, so first I'd check whether session is needed at all. In FeeBilling, a search of the legacy code finds no session usage, so I wouldn't add it.

**Interviewer:** How do you test an ASP.NET Core service whose authentication depends on another app?

[pause 5s]

In `WebApplicationFactory`, I replace the authentication scheme with a test handler, like FeeBilling's `TestAuthHandler`, which builds a user from a header, and I include a test that an anonymous request gets a 401. Then I'm explicit that this doesn't test the integration itself, and I cover the real handshake with a separate environment-level test that runs with the legacy app up.

## Recap

Five things to remember from this lesson.

One: there are three ways to share a user between old and new apps: remote authentication, a shared cookie, or an external identity provider first. FeeBilling uses remote authentication because Forms tickets can't be read by ASP.NET Core.

Two: FrameworkServices runs in IIS and CoreServices runs in ASP.NET Core; the shared package lets `netstandard2.0` libraries compile against `System.Web`.

Three: every authenticated request costs a round trip to legacy, couples the new service's availability to legacy, and grows legacy's load as migration progresses.

Four: don't add remote session unless code actually uses session. FeeBilling's doesn't.

Five: close the gaps: rotate and move the committed API key, block the remote-app endpoints at the gateway, keep health checks independent of legacy, and test the real handshake separately from the fake-identity tests.

In the next lesson, we'll look at what breaks silently when an endpoint moves from Web API 2 to ASP.NET Core: JSON casing, data sets and dates.
