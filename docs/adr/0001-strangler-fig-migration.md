# ADR-0001: Incremental strangler-fig migration off .NET Framework

- **Status:** Accepted
- **Date:** 2025-03-04
- **Deciders:** Modernization team, Head of Engineering, Product (Billing)

## Context

FeeBilling was built between 2011 and 2016 on .NET Framework (ASP.NET MVC 5 + Web API 2, WCF, EF6 Database-First, and a Windows Service), and it has been maintained rather than evolved since. It calculates and collects advisory fees for about 40 wealth-management firms (roughly 350,000 accounts).

Enterprise clients now require:

- Azure-hosted, containerized deployments
- SSO against their own identity provider
- SOC 2 evidence that is painful to produce from the current stack

None of that is practical on .NET Framework and IIS.

We considered two approaches:

1. **Big-bang rewrite:** build a new platform in parallel and cut over all firms at once.
2. **Incremental strangler fig:** put a reverse proxy in front of the legacy app, then migrate one route or component at a time, with each step independently deployable and reversible.

The system bills money. A billing defect is a client-facing, regulator-visible incident. The legacy test suite is not trustworthy.

## Decision

Migrate incrementally using the strangler-fig pattern:

- A reverse proxy (see [ADR-0003](0003-yarp-gateway.md)) sits in front of all traffic. Migrated routes go to new services; everything else falls through to legacy.
- New services target modern .NET (see [ADR-0002](0002-target-dotnet-version.md)) and run in containers.
- Shared domain types are extracted into a `netstandard2.0` library (`FeeBilling.Domain`) so legacy and new code can use them during the transition.
- Legacy and new services share the existing database during the transition (see [ADR-0004](0004-shared-database-during-transition.md)).
- Authentication stays with the legacy app for now. New services resolve the user through `Microsoft.AspNetCore.SystemWebAdapters` remote authentication.
- We migrate low-risk, read-mostly areas first (Accounts, Households) to prove the pipeline, before touching fee calculation.

## Consequences

**Positive**

- Every step can be deployed and rolled back on its own, and the business sees value early.
- Hard problems surface one at a time instead of all at cutover.

**Negative**

- Two stacks, two ORMs and a shared database have to be run and understood at once, for a long time.
- The transitional auth (remote authentication) couples new services to legacy uptime and adds a round trip per request.
- Each migrated route needs proof that it behaves like the legacy route, which is extra work per slice.
