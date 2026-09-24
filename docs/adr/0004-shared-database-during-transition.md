# ADR-0004: Share the existing database during the transition

- **Status:** Accepted
- **Date:** 2025-04-02
- **Deciders:** Modernization team, DBA

## Context

Legacy (EF6 Database-First via `FeeBilling.edmx`, plus stored procedures and ADO.NET) and the new services need the same data: accounts, households, fee schedules, positions, runs and invoices.

Options considered:

1. **Database per service from day one:** requires data synchronization (CDC, dual writes or events) between legacy and new before anything useful ships.
2. **Share the existing `FeeBilling` database:** new services map the existing tables with EF Core, reverse-engineered from the live schema.

## Decision

New services use the **existing `FeeBilling` database** during the transition:

- EF Core models are reverse-engineered (`dotnet ef dbcontext scaffold`) and trimmed to what each service needs.
- New services start **read-mostly**. Accounts.Api is read-only.
- Schema changes are coordinated manually between the legacy and modernization teams for now.

## Consequences

- We can ship the first migrated endpoints quickly, with no data synchronization.
- Two models (the EDMX and EF Core) describe the same tables and can drift.
- Any schema change can break the legacy app, the new services, or both.
- Database-per-service is deferred, not abandoned.

## Open questions

- Who owns schema changes, and which model is the source of truth?
