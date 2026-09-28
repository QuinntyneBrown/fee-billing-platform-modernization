## Project Overview

A practice repository for incrementally migrating FeeBilling, a fictional wealth-management fee-billing platform, from .NET Framework 4.7.2 to .NET 10 using the strangler-fig pattern. `legacy/` holds the original system, deliberately left as found. `src/` holds a migration that stalled about 30% of the way through: a YARP gateway and a migrated Accounts API, which share one SQL Server database with the legacy app. The work packages (WP-01 to WP-10) in `docs/brasswick-modernization-training-plan.md` are still undone, and the known issues are listed in `docs/handover.md`.

## Commands

- Build: `dotnet build legacy/FeeBilling.sln -c Release`
- Test: `dotnet test legacy/FeeBilling.Tests -c Release --no-build`

## Project Structure

- `.github/`
- `.github/workflows/`
- `database/`
- `database/billing/`
- `database/reporting/`
- `database/seed/`
- `docker/`
- `docker/servicebus/`
- `docs/`
- `docs/adr/`
- `legacy/`
- `legacy/FeeBilling.BillingRunner/`
- `legacy/FeeBilling.Core/`
- `legacy/FeeBilling.Data/`
- `legacy/FeeBilling.Ingestion.Wcf/`
- `legacy/FeeBilling.Invoicing/`
- `legacy/FeeBilling.Tests/`
- `legacy/FeeBilling.Web/`
- `seed/`
- `seed/custodian-files/`
- `src/`
- `src/FeeBilling.Accounts.Api/`
- `src/FeeBilling.Domain/`
- `src/FeeBilling.Gateway/`
- `src/FeeBilling.Infrastructure/`
- `src/FeeBilling.ServiceDefaults/`
- `tests/`
- `tests/FeeBilling.Accounts.Api.Tests/`
- `tests/FeeBilling.Domain.Tests/`
- `tools/`
- `tools/FeeBilling.DbInit/`

## Testing

- Tests use mstest.
- Run them with `dotnet test legacy/FeeBilling.Tests -c Release --no-build`.

## Code Style

- `.editorconfig` is authoritative for formatting.
- `Directory.Build.props` is authoritative for build.
- `Directory.Packages.props` is authoritative for packages.

## Git Workflow

- Every change is checked by continuous integration:
  - `.github/workflows/ci.yml`

## Boundaries

