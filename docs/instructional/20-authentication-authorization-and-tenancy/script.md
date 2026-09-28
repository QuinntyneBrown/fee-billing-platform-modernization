# 20 · Authentication, Authorization and Tenancy

Welcome. This lesson is about identity: who the caller is, what they're allowed to do, and which firm's data they're allowed to see. Enterprise clients want to sign in with their own identity provider, and every one of them will ask how you stop Firm A from seeing Firm B's invoices. By the end of this lesson, you should be able to explain how you'd move FeeBilling from Forms authentication to OpenID Connect without logging everyone out, why endpoints should reference policies instead of roles, and how you'd enforce tenant isolation in a way a test can actually prove.

## The questions this lesson answers

Here are the questions this lesson prepares you for. How do you migrate from Forms authentication to OIDC without logging everyone out? How do you enforce tenant isolation? What's the difference between roles and policies? Where should tokens live in a browser application? And a judgment question: what would you fix first in this legacy authentication?

Let's start with two facts from the code, because they frame everything else. First, the seed data has a user called `maple.ops`. That user belongs to firm one, Maple Ridge, and has only the `BillingReviewer` role. Yet that user can start a billing run for any firm, because the start-run endpoint only checks that the caller is signed in. Second, the same user can list all five hundred accounts belonging to Groupe Financier Laurentien, a different firm, through the new, already migrated Accounts API, just by changing the firm ID in the query string from one to two. Nothing in the system checks which firm the caller belongs to. That's the headline, and it's the first thing you'd fix.

## What legacy security actually checks

Before you design anything new, inventory what exists. For a Forms authentication application, that means six things: the cookie, the machine key, the membership and role providers, password storage, lockout, and anti-forgery.

The cookie is configured in `legacy/FeeBilling.Web/Web.config`. It's a Forms authentication cookie named FeeBilling Auth, in capitals with a leading dot, with a timeout of one hundred and twenty minutes and sliding expiration turned on. That timeout matters later, because it tells you how long old sessions can live after you switch sign-in methods.

The cookie is encrypted and signed with the `machineKey`, and that key is committed to source control in `Web.config`. There's a second one, for a single-tenant client, in `Web.ClientX.config`. Any key that has ever been committed must be treated as compromised and rotated, along with the database passwords sitting next to it in the same transform files.

Users live in a table called `AppUsers`, read by a custom membership provider, `FeeBillingMembershipProvider`, over plain ADO.NET. Open `legacy/FeeBilling.Web/Security/FeeBillingMembershipProvider.cs` and look at the lockout setting. `MaxInvalidPasswordAttempts` returns `int.MaxValue`, with the comment "no lockout". So an attacker can guess passwords forever.

Password storage is in `PasswordHasher.cs`. It's SHA-1 of the salt concatenated with the password, base64 encoded. There's a salt, which is good, but there are no iterations, so it's fast to brute-force, and SHA-1 is not a password hash. The verify method compares the computed hash with the stored one using a plain string equality check, which isn't constant-time. The comment in the file says so honestly: not constant-time.

Roles come from a custom role provider, and the `roleManager` element in `Web.config` sets `cacheRolesInCookie` to true. That's a performance optimization with a security cost: if you revoke someone's role, they keep it until their roles cookie expires.

Now authorization. `legacy/FeeBilling.Web/App_Start/WebApiConfig.cs` adds a global `AuthorizeAttribute` to every Web API controller. The comment says everything under the API requires a Forms authentication cookie. That means authenticated. It doesn't mean authorized. Only a few actions add a role on top. In `BillingController.cs`, the approve action requires the `BillingAdmin` role, but `StartRun` has no role check at all. In `FeeSchedulesController.cs`, create and update require the `ScheduleEditor` role, and there's a comment worth quoting: no audit trail beyond ModifiedBy, ticket FB-340.

Finally, anti-forgery. In `legacy/FeeBilling.Web/App/services/api.service.js` there's a to-do, FB-279: POSTs don't send the anti-forgery token, and Web API doesn't check it anyway. Remember that one. It becomes critical once we move to cookies done properly.

So the inventory reads: weak password storage, no lockout, committed keys, stale roles, and above all, no firm check anywhere. The data model even has the concept: `AppUsers` has a `FirmId` column, and the schema comment in `database/billing/001-schema.sql` says a null firm means vendor staff who see all firms. The column exists. Nothing enforces it.

## The target: OIDC with the gateway as a backend for frontend

The target design has three parts.

First, identity moves to an external identity provider using OpenID Connect. For this vendor, that's Microsoft Entra ID, designed so that each enterprise client can federate its own identity provider. Their employees sign in with their own corporate accounts, and FeeBilling never stores their passwords.

Second, the gateway becomes a backend for frontend, or BFF. The gateway runs the OIDC authorization code flow. When sign-in completes, the gateway holds the tokens, either inside its encrypted authentication ticket or in a server-side session store, and it gives the browser a cookie. That cookie is `HttpOnly`, so JavaScript can't read it, `Secure`, so it only travels over HTTPS, and `SameSite`, so the browser limits when it's sent cross-site. The access token never reaches JavaScript, which shrinks the damage a cross-site scripting bug can do.

Third, when the gateway forwards a request to an API, a YARP request transform attaches the access token as a bearer header. Each API validates that token with JWT bearer authentication: it checks the issuer, the audience, the signature and the expiry.

There's a simpler alternative worth naming: the APIs could trust identity headers set by the gateway instead of validating tokens themselves. That's only safe if the APIs are unreachable except through the gateway. If anyone can reach an API directly, they can forge the header. Token validation is defence in depth.

Two details make the BFF work in production. Access tokens expire, typically after about an hour, so the BFF has to refresh them, or sessions break silently. A library such as Microsoft.Identity.Web can handle that, and you should choose one deliberately. And saved tokens make the cookie large, so a server-side ticket store, which keeps only a session identifier in the browser, is often the better choice.

The `machineKey` also has a modern replacement: the ASP.NET Core Data Protection key ring. Every app that must read the same cookie uses the same application name and the same persisted key ring, for example keys stored in Blob Storage and encrypted with a Key Vault key. Without a persisted key ring, every container restart or scale-out invalidates every cookie. That's the container version of mismatched machine keys across a web farm.

## The transition: signing in once

You can't switch every firm to a new sign-in method on one night. So the transition needs a plan.

Start by mapping existing users to identity provider identities. That needs a reconciliation step, because `Email` is nullable in `AppUsers`, so you can't just match on email.

Then switch sign-in per firm with a flag. Firms that have moved sign in through OIDC; the rest keep Forms authentication for now.

The hard requirement is that users sign in once. While legacy still serves pages, it has to accept the new identity. There are two ways. Legacy can become an OIDC client of the same identity provider, so moving between old and new pages is a silent single sign-on redirect, with no second password. Or both applications can read one shared cookie, which in practice means switching legacy to OWIN cookie authentication and sharing a Data Protection key ring with the new apps. As of September 2026, check the current interop packages and guidance before committing to the shared-cookie route; the single sign-on route has fewer moving parts. Lesson ten covers how SystemWebAdapters bridges identity today.

And you let old Forms cookies expire naturally. With a hundred-and-twenty-minute sliding timeout, they're gone within a day of a firm's switch. What you never do is force a password reset as the migration mechanism. That's a support incident, not a plan.

If some local passwords survive, for example for vendor staff, verify the old SHA-1 hash once, using a constant-time comparison such as `CryptographicOperations.FixedTimeEquals`, and on that successful login, rehash the password with ASP.NET Core Identity's `PasswordHasher`, which uses PBKDF2. Better still, move those users to the identity provider too.

## Roles become policies

A role is a fact about a user: this person is a billing admin. A policy is a rule for an action: may this caller start a billing run? Endpoints should reference policies, not roles.

Here's why. If ten endpoints say "require BillingAdmin", and the business decides reviewers may also start runs, you change ten places. If ten endpoints say "require the CanStartBillingRun policy", you change one. Policies can also combine things roles can't: a role, a claim, and a check against the resource being accessed.

For FeeBilling, the mapping looks like this. `CanViewBilling` replaces the global authorize filter, and requires the reviewer or admin role plus either a firm claim or the vendor-staff flag. `CanStartBillingRun` has no legacy equivalent, because today any signed-in user can start a run; it requires `BillingAdmin`, and the run's firm must match the caller's firm. `CanApproveInvoices` replaces the admin role check on approve, and you might add a four-eyes rule, so the person who started a run can't approve it. `CanEditFeeSchedules` replaces the schedule editor role check, and the schedule's firm must match, bearing in mind that platform-wide template schedules have no firm.

In ASP.NET Core, you define these with `AddAuthorizationBuilder`, add each policy with `AddPolicy`, and attach them to endpoints with `RequireAuthorization` and the policy name. Also set a fallback policy that requires an authenticated user, so a newly added endpoint is never accidentally anonymous.

"May this user start a run for this firm?" is a resource-based check. The role alone can't answer it, because it depends on the firm in the request. That's the bridge to tenant isolation.

One trap here catches almost everyone. With Entra ID, when you turn off inbound claim mapping, app roles arrive in a claim called `roles`. Unless you set the token validation's `RoleClaimType` to roles, `RequireRole` never matches, and every call returns 403. Leave mapping on and you get the opposite problem: long schema URLs as claim types in some places and short names in others.

## Tenant isolation you can prove

Now the most important section. Open `src/FeeBilling.Accounts.Api/Accounts/AccountsEndpoints.cs`. The list method, `ListAccounts`, takes `firmId` as a parameter bound from the query string, and filters accounts where the firm matches it. The caller chooses the firm. That's an insecure direct object reference, an IDOR. A value in a query string is a filter the caller controls, not an authorization decision. The households endpoint does the same thing, and the get-by-ID endpoint doesn't check the firm at all.

You can prove it with a test. In the Accounts API test project, create an authenticated client as `maple.ops`, request accounts with firm ID two, and assert the result is empty. Today that test fails, because it returns Laurentien's five hundred accounts. One wrinkle: the test authentication handler in `tests/FeeBilling.Accounts.Api.Tests/Infrastructure/TestAuthHandler.cs` only issues a name claim. Part of the fix is teaching it to issue firm and role claims, so tests exercise the same policies production uses.

The fix comes in layers. First, the tenant comes from a trusted claim, never from the request. A small `ITenantContext` service reads the `firm_id` claim from the validated token. Second, endpoints stop accepting a firm ID from callers who have one. Third, the data layer enforces it anyway, as a backstop, with EF Core global query filters. The DbContext captures the tenant's firm in a field, and the account entity gets a filter that only returns rows for that firm. EF Core evaluates filters that reference context fields per context instance, which is why the tenant is captured in a field.

EF Core 10 adds named query filters, so a tenant filter and a soft-delete filter can be ignored independently. Before that, an entity could have only one filter, so ignoring filters removed both. Check the exact overloads for your version.

Vendor staff are real users, with a null firm. Model them as an explicit, logged mode, not as "no filter". Otherwise a support engineer's query silently becomes cross-tenant, and nobody can tell afterwards.

Then know the ways around a query filter, because an interviewer will ask. `IgnoreQueryFilters` turns it off. Raw SQL with `FromSql` or `SqlQuery` doesn't apply it. Stored procedures know nothing about it: `usp_GetBillableAum` takes an account ID and trusts it. Dapper bypasses EF Core entirely. And DbContext pooling can hand one tenant's cached context state to another request if the tenant is captured at construction. Treat each of these as a reviewed exception, backed by an architecture test or analyzer.

Finally, the test that makes isolation provable. Instead of writing one test per endpoint, write one test that discovers every GET endpoint from ASP.NET Core's `EndpointDataSource`, calls each one as a firm one user with firm two identifiers, and asserts every response is either not found or forbidden. Because the test discovers endpoints, an endpoint added next month is covered automatically. That's the property you want: data-layer enforcement, plus proof at the HTTP layer.

## The audit trail

Fee schedules decide what clients pay, and FB-340 says there's no audit trail beyond `ModifiedBy`. The update action even has a comment: changing a schedule mid-quarter changes every open invoice that uses it, and nothing stops that.

A proper audit record captures who, when, which schedule, and the before and after values of every tier. "Who" should be the immutable subject identifier from the token, not a display name, because names change. There are two good implementations. A `SaveChangesInterceptor` can write an append-only audit table in the same transaction as the change. Or SQL Server temporal tables keep full row history, and EF Core supports them with `IsTemporal`. Either way, the audit feeds reconciliation at cutover, which is lesson twenty-three.

## Traps

Taking the tenant from the request. A firm ID in a query string or body is something the caller controls. Accounts API already shipped this mistake.

Treating authenticated as authorized. Legacy start-run relied on the global authorize filter. Porting it as-is ports the hole.

Getting the role claim type wrong, so every call returns 403, or leaving claim mapping on and ending up with two naming schemes.

Modelling vendor staff as "no filter" instead of an explicit, audited mode.

Forgetting the filter bypasses: ignore-filters calls, raw SQL, stored procedures, Dapper, and pooled contexts.

Forgetting that cookies bring cross-site request forgery back. Moving tokens out of the browser is right, but it makes FB-279 critical. Use antiforgery plus SameSite, which lesson twenty-two covers.

Not saying how long revocation takes. Roles cached in cookies, and access tokens, both survive until they expire.

And auditing by display name.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do you migrate from Forms authentication to OIDC without logging everyone out?

[pause 5s]

First, map existing users to identity provider identities, with a reconciliation step because not every user has an email. Then run both sign-in paths for a window, switched per firm by a flag. While legacy still serves pages, make it trust the new identity, either as an OIDC client of the same provider, so moving between apps is a silent single sign-on, or through a shared cookie and Data Protection key ring. Old Forms cookies expire naturally; here the timeout is a hundred and twenty minutes. I'd never use a forced password reset as the migration mechanism.

**Interviewer:** How do you enforce tenant isolation?

[pause 5s]

The tenant comes from a trusted claim in the validated token, never from the query string or body. I enforce it in the data layer with EF Core global query filters, and I prove it at the HTTP layer with a test that discovers every endpoint and checks that a user from one firm gets not found or forbidden for another firm's data. Filter bypasses, like ignore-filters calls, raw SQL and stored procedures, are reviewed exceptions. Vendor staff who see all firms are an explicit, audited mode, not the absence of a filter.

**Interviewer:** Roles or policies?

[pause 5s]

A role is a fact about a user; a policy is a rule for an action, like CanStartBillingRun. Endpoints reference policies, so changing who may do something is one change in one place, and a policy can combine roles, claims and a check against the resource, such as whether the run's firm matches the caller's firm. I'd also set a fallback policy so nothing is accidentally anonymous.

**Interviewer:** Where should tokens live in a browser application?

[pause 5s]

Nowhere JavaScript can read them. The gateway acts as a backend for frontend: it runs the OIDC flow, keeps the tokens server-side or in its encrypted ticket, and gives the SPA an HttpOnly, Secure, SameSite cookie. That limits what a cross-site scripting bug can steal. But cookies bring back cross-site request forgery, so I'd add antiforgery tokens on every state-changing request.

**Interviewer:** What would you fix first in this legacy authentication?

[pause 5s]

The missing tenant check, because any signed-in user can read any firm's accounts through the migrated Accounts API, just by changing a query-string value. Second, the missing role check on starting a billing run. I'd also rotate the committed machine keys and passwords immediately. The weak SHA-1 password hashing matters less if passwords move to the identity provider, which is where they're going.

## Recap

Five things to remember from this lesson.

One: authenticated is not authorized. Legacy checks that you're signed in, and in most places nothing more, and it never checks your firm.

Two: the target is OIDC with the gateway as a backend for frontend. Tokens stay out of the browser, the browser gets a secure cookie, and APIs validate bearer tokens.

Three: during the transition, users sign in once. Switch firms one at a time, make legacy trust the new identity, and let old cookies expire.

Four: endpoints reference policies, not roles, with a fallback policy so nothing is accidentally anonymous.

Five: the tenant comes from a claim, is enforced by query filters in the data layer, and is proven by a test that walks every endpoint.

In the next lesson, we'll look at `System.Drawing` and PDF generation: what to do with a Windows-only dependency, and three bugs that IIS hid for years.
