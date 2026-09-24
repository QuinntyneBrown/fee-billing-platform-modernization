# Modernization Handover

**From:** the previous modernization team (reassigned to the Reporting platform)
**To:** whoever picks up the FeeBilling modernization next
**Status:** roughly 30% of the way through a strangler-fig migration off .NET Framework 4.7.2

We didn't get as far as we hoped. This note covers what exists, what doesn't, and what we know is broken. Start with [ADR-0001](adr/0001-strangler-fig-migration.md) for the overall approach.

---

## Where things are

| Area | State | Notes |
|---|---|---|
| `legacy/` | Unchanged, apart from the SystemWebAdapters remote-app server added to `Global.asax` | Still the system of record for everything except Accounts/Households reads |
| `src/FeeBilling.Domain` | Done | `netstandard2.0` so legacy `FeeBilling.Core` can consume it. Entities + value objects only |
| `src/FeeBilling.Gateway` | Done | YARP. `/api/accounts` and `/api/households` go to Accounts.Api; everything else falls back to legacy IIS |
| `src/FeeBilling.Accounts.Api` | Done | Accounts + Households endpoints. Same JSON shape as Web API 2 (PascalCase) |
| `src/FeeBilling.Infrastructure` | Partial | EF Core model for Firms/Accounts/Households. FeeSchedule/FeeTier configurations scaffolded but not wired |
| `src/FeeBilling.ServiceDefaults` | Partial | Health checks work. OpenTelemetry is configured but has no exporter |
| `tests/FeeBilling.Accounts.Api.Tests` | Done | xUnit + WebApplicationFactory + Testcontainers |
| `tests/FeeBilling.Domain.Tests` | Partial | Value objects only |
| Fee schedules, billing runs, invoices | Not started | Still served by legacy through the gateway fallback |
| BillingRunner (Windows Service) | Not started | |
| Custodian ingestion (WCF) | Not started | Three client firms' feed agents can't be upgraded for ~6 months |
| AngularJS front end | Not started | |

## Auth during the transition

Legacy still owns login (Forms auth, custom membership tables). The new services use `Microsoft.AspNetCore.SystemWebAdapters` **remote authentication**: on every request, Accounts.Api calls back to the legacy app to resolve the user. It works, but it adds a round trip per request and ties every new service to legacy uptime. If IIS is down, Accounts.Api returns 500 on everything, including `/health`.

The integration tests replace remote auth with a header-based test scheme (`tests/FeeBilling.Accounts.Api.Tests/Infrastructure/TestAuthHandler.cs`).

## Known issues

1. `ADR-0002` targets .NET 8; support ends November 10, 2026.
2. `FeeSchedule.Tiers` EF Core mapping uses `decimal(18,2)` for `AnnualRate`. The DB column is `decimal(9,6)`. Rates like 0.0075 would truncate to 0.01 on write. **Nothing writes yet, so nobody noticed.**
3. The Accounts API returns `DateTime` with `Kind=Unspecified`; the AngularJS app interprets them as local time. Dates off by one for users west of UTC after 8 p.m.
4. Both the EDMX and EF Core model describe the same tables. No one decided who owns schema changes.
5. No parity tests exist for anything fee-related. "We were going to do that next."

## Things we'd have done next

- Characterization tests on the fee calculators before touching them. The MSTest suite in `legacy/FeeBilling.Tests` is not a safety net: 61 tests, 9 failing, 22 ignored.
- Billing.Api (fee schedules, runs, invoices) behind the gateway.
- Replace the BillingRunner Windows Service. It uses MSDTC, which isn't available where we want to host.

## Useful links

- Training plan / project brief: [`brasswick-modernization-training-plan.md`](brasswick-modernization-training-plan.md)
- ADRs: [`docs/adr/`](adr/)
- Local setup: [`README.md`](../README.md)
