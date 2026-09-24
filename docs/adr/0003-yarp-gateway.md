# ADR-0003: YARP as the front door during the migration

- **Status:** Accepted
- **Date:** 2025-03-18
- **Deciders:** Modernization team, Infrastructure

## Context

The strangler fig ([ADR-0001](0001-strangler-fig-migration.md)) needs a single entry point that can send each route to either the legacy IIS application or a new service. Changing routing should be a configuration change, not a code change in the legacy app.

Options considered:

- **IIS URL Rewrite / ARR** on the legacy servers: keeps us tied to IIS, which is what we're leaving.
- **Azure Front Door / Application Gateway path routing:** fine in Azure, but doesn't help local development or single-tenant client installs.
- **YARP** (Microsoft's reverse proxy library for ASP.NET Core): runs anywhere .NET runs, config-driven, and extensible in C# (transforms, auth, correlation) when we need it.

## Decision

Build `FeeBilling.Gateway` as an ASP.NET Core app hosting **YARP**, configured from `appsettings.json`:

- One route per migrated path prefix, pointing at the owning service's cluster.
- A **catch-all fallback route** (`{**catch-all}`, `Order: 1000`) to the `legacy-iis` cluster, so anything not yet migrated keeps working unchanged.
- Migrating a route = add a route entry + deploy. Rolling back = remove it.

## Consequences

- All browser and API traffic goes through the gateway, so it has to be highly available and observable.
- The gateway is the natural place for cross-cutting concerns (auth, correlation IDs, rate limiting) later on.
- Legacy URLs stay stable for clients, because the new services must serve the same paths and shapes, or the gateway has to transform them.
