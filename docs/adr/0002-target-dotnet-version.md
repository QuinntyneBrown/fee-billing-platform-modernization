# ADR-0002: Target .NET version for migrated services

- **Status:** Accepted
- **Date:** 2025-03-11
- **Deciders:** Modernization team

## Context

New services built under the strangler-fig migration ([ADR-0001](0001-strangler-fig-migration.md)) need a target framework. At the time of writing:

| Version | Type | End of support |
|---|---|---|
| .NET 8 | LTS | November 10, 2026 |
| .NET 9 | STS | May 2026 |

Enterprise clients and our security team want an LTS release. Some of the libraries we need (YARP, SystemWebAdapters, EF Core) support both .NET 8 and .NET 9.

Shared libraries have to stay consumable by the .NET Framework 4.7.2 legacy app during the transition.

## Decision

- New services target **.NET 8 (LTS)**.
- Shared libraries consumed by legacy target **`netstandard2.0`** until no .NET Framework project references them.
- We don't adopt STS releases.

## Consequences

- We get the longest support window available today.
- Shared `netstandard2.0` libraries can't use newer APIs (`DateOnly`, `init`-only setters without polyfills, and so on).
- We'll revisit this when the next LTS ships.
