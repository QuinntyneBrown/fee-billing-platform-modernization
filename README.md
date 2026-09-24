# fee-billing-platform-modernization

A practice repository for an incremental .NET Framework → .NET 10 migration of **FeeBilling**, a (fictional) wealth-management fee-billing platform. The brief is in [`docs/brasswick-modernization-training-plan.md`](docs/brasswick-modernization-training-plan.md).

The repo is in its **"as found" state**: the legacy system (Section 6 of the brief) plus a stalled strangler-fig migration about 30% done (Section 7), including the known issues in [`docs/handover.md`](docs/handover.md). **None of the work packages (WP-01 to WP-10) have been done.** That's the exercise.

## What's here

```
legacy/                      .NET Framework 4.7.2 solution (FeeBilling.sln), deliberately left as found
  FeeBilling.Web               MVC 5 + Web API 2 + AngularJS 1.6 SPA
  FeeBilling.Core              fee calculators, AUM, householding, billing runs
  FeeBilling.Data              EF6 EDMX, reporting context, ADO.NET
  FeeBilling.Ingestion.Wcf     custodian feed SOAP service (BinaryFormatter staging)
  FeeBilling.BillingRunner     Windows Service (TransactionScope over two databases)
  FeeBilling.Invoicing         System.Drawing + Framework-only PDF
  FeeBilling.Tests             MSTest: 61 tests, 9 failing, 22 ignored
src/                         .NET 10 (FeeBilling.Modern.slnx)
  FeeBilling.Domain            netstandard2.0, shared with legacy
  FeeBilling.Gateway           YARP front door (:5000)
  FeeBilling.Accounts.Api      migrated Accounts + Households endpoints (:5101)
  FeeBilling.Infrastructure    EF Core 10 (partial)
  FeeBilling.ServiceDefaults   health checks + OpenTelemetry (no exporter)
tests/                       xUnit v3: Domain.Tests, Accounts.Api.Tests (WebApplicationFactory + Testcontainers)
tools/FeeBilling.DbInit      creates and seeds the databases on any SQL Server
database/                    schema, 14 stored procedures, reporting schema, Q3 2026 seed (Section 10)
seed/custodian-files/        S12 position files (Windows-1252, fr-CA formatting)
docs/                        brief, handover, ADRs 0001-0004
docker-compose.yml           SQL Server, Azurite, Service Bus emulator
```

## Prerequisites

- .NET SDK **10.0.401+** (pinned in `global.json`)
- Docker
- Windows, to *run* the legacy code and its tests. Everything *builds* anywhere.

## Getting started

```bash
# 1. Infrastructure
docker compose up -d

# 2. Create and seed FeeBilling + FeeBillingReporting (idempotent; --reseed to start over)
dotnet run --project tools/FeeBilling.DbInit

# 3. Legacy
dotnet build legacy/FeeBilling.sln
dotnet test  legacy/FeeBilling.Tests            # Failed 9, Passed 30, Skipped 22 (as found)

# 4. Modern
dotnet build FeeBilling.Modern.slnx
dotnet test  FeeBilling.Modern.slnx             # starts its own SQL container via Testcontainers
```

### ARM64 machines (Apple Silicon, Windows on ARM)

The SQL Server 2022 container image is amd64-only. Copy `.env.example` to `.env` and uncomment `SQL_IMAGE=mcr.microsoft.com/azure-sql-edge:latest`. The integration tests pick Azure SQL Edge automatically on ARM64; override with `FEEBILLING_TEST_SQL_IMAGE`. You can also point DbInit at a local instance:

```bash
dotnet run --project tools/FeeBilling.DbInit -- --connection "Server=.\SQLEXPRESS;Integrated Security=true;TrustServerCertificate=true"
```

### Running the services

```bash
dotnet run --project src/FeeBilling.Accounts.Api    # http://localhost:5101
dotnet run --project src/FeeBilling.Gateway         # http://localhost:5000
```

The gateway routes `/api/accounts` and `/api/households` to Accounts.Api, and everything else to the legacy IIS app at `http://localhost:8080`. Accounts.Api authenticates every request by calling back into the legacy app (SystemWebAdapters remote authentication), so **without the legacy app running it returns 500**. That's the transition coupling described in the handover. The integration tests swap in a test auth scheme.

## Seed data

Period: Q3 2026 (2026-07-01 to 2026-09-30, 92 days). The account-to-scenario map is at the top of [`database/seed/010-seed-q3-2026.sql`](database/seed/010-seed-q3-2026.sql).

| Firm | Accounts | Scenarios |
|---|---|---|
| 1 `MAPLE` Maple Ridge Wealth Partners | 1001–1013, households H-100 and H-200 | S1–S10 |
| 2 `LAURENT` Groupe Financier Laurentien | 2001–2500 (generated, deterministic) | S11, S12 |

Legacy logins (Forms auth): `admin` / `Billing2026!` and `maple.ops` / `Billing2026!`.

## Where to start

1. Read [`docs/handover.md`](docs/handover.md) and the ADRs in [`docs/adr/`](docs/adr/).
2. Section 3 of the brief has the 10-day and 4-day tracks. **Start with WP-01 (golden master).**
3. One branch per work package; each ends with an ADR (`docs/adr/00NN-*.md`) and an updated parity report.
