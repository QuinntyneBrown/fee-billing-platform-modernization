# 01 · Migration Strategy: Strangler Fig vs Big Bang

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work packages:** framing for all of WP-01 to WP-10 · **Prerequisites:** none

**Audio lesson:** [01-migration-strategy-strangler-fig.mp3](01-migration-strategy-strangler-fig.mp3) · [Transcript](script.md)

## Why this video exists

"How would you approach modernizing a large .NET Framework application?" is the opening question in almost every modernization interview. A weak answer lists technologies. A strong answer is a **sequence**, where each step reduces risk before the next, and every step can be deployed and rolled back on its own. This video builds that answer from the FeeBilling code: seven legacy projects, each blocked from modern .NET for a different reason, and a migration that stalled at about 30%.

## Learning objectives

By the end, the viewer can:

- Explain the strangler-fig mechanics (a facade in front, migrate one route at a time, a catch-all fallback to legacy) and say when a big-bang rewrite is justified.
- Build a legacy inventory and risk register from source code, grouping each component by *why* it can't move as-is.
- Sequence a migration: why read-mostly Accounts went first, why the fee engine can't move before a golden master exists, and why the billing worker is among the last to move.
- Define "done" for a migrated slice: routed, proven equivalent, observable, reversible, and the legacy path retired.
- Critique inherited decisions (ADRs) and supersede them properly.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How would you approach modernizing a large .NET Framework application? | Inventory first. Retarget shared libraries to `netstandard2.0`. Put a facade (YARP) in front. Migrate low-risk slices first. Add characterization tests *before* high-risk logic moves. Every step is independently deployable and reversible. Cut over tenant by tenant. |
| Big bang or incremental? | Incremental, unless the app is small and well-tested. Big bang delivers no value until the end, surfaces every hard problem at once, and can only be rolled back all at once. The cost of incremental: two stacks, a shared database and transitional auth for a long time. |
| What would you migrate first, and why? | Something read-mostly, low-risk and on a real traffic path, so it proves the pipeline end to end (gateway, auth, data access, CI, deploy). Never the fee engine first. |
| How do you know a slice is finished? | Traffic is routed to it, contract and parity tests pass, it is observable, rollback has been exercised, and the legacy code path is deleted or frozen. |
| You inherit a migration that stalled at 30%. What do you do in week one? | Read the ADRs and handover, then verify them against the code (ADR-0002 is already wrong). Build a risk register. Start the parity harness. Handle the time-critical item: .NET 8 support ends November 10, 2026. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.sln`, `legacy/README.md` | The 7 legacy projects, plus `src/FeeBilling.Domain` that legacy now references |
| `docs/adr/0001-strangler-fig-migration.md` | Decision and **negative** consequences (two stacks, remote-auth coupling) |
| `src/FeeBilling.Gateway/appsettings.json` | `accounts` and `households` routes, `fallback` with `"Order": 1000` |
| `legacy/FeeBilling.Web/Controllers/Api/AccountsController.cs` | `// MIGRATED ... Kept for rollback. Do not add features here.` |
| `docs/handover.md` | The five known issues: what "30% done" really looks like |
| `.github/workflows/ci.yml` | Two jobs (`windows-latest` legacy, `ubuntu-latest` modern), legacy tests `continue-on-error: true` |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | The thesis: *"In fee billing, the migration is only done when you can prove the new system produces the same invoice, to the cent, for every account, and explain every place it intentionally doesn't."* Everything in the series serves that sentence. |
| 01:30–04:00 | Big bang vs strangler fig | Big bang's three failure modes: value arrives late, risk arrives all at once, rollback is all-or-nothing. When big bang *is* fine: small app, good tests, no shared state. Strangler fig: a facade, route-by-route replacement, a fallback that keeps everything else working. |
| 04:00–08:00 | Live inventory | Run the `git grep` from the demo. Fill in the risk register below on screen, one row per project. Point out that every project is blocked for a *different* reason, so there is no single "upgrade" step. |
| 08:00–11:00 | Strangler mechanics in this repo | Gateway catch-all at `Order: 1000`. Migrating = add a route; rolling back = remove it. The legacy `AccountsController` is kept for rollback. Shared database (ADR-0004). Remote auth calls back into legacy, so Accounts.Api returns 500 when IIS is down. Show it. |
| 11:00–14:30 | Sequencing | Draw the dependency order: Domain extraction → Gateway → Accounts (done) → **golden master (WP-01)** → fee engine (WP-02) → Billing API (WP-03) → worker (WP-04) → ingestion (WP-05) → data, config, auth, UI (WP-06 to WP-09) → cutover (WP-10). Key rule: a *migration change* and a *behaviour change* must never ship together, or you can't tell which one moved a client's invoice. |
| 14:30–17:00 | Definition of done and reversibility | Reads are trivially reversible through routing. **Writes are not.** If the new service writes rows, legacy must still be able to read them after a rollback, so schema changes must be backward compatible (expand/contract). Per-firm feature flags make rollback granular. |
| 17:00–19:00 | Reviewing inherited decisions | ADR-0002 targets .NET 8, which reaches end of support on November 10, 2026. ADR-0004 leaves "who owns the schema?" open. Don't edit an accepted ADR in place: write a new ADR that supersedes it and mark the old one as superseded. |
| 19:00–20:00 | Recap | The interview answer as a 30-second sequence. Preview video 04 (golden master), the step the previous team skipped. |

### Risk register to build on screen

| Component | Why it can't move as-is | Modern target | Risk | Why that risk |
|---|---|---|---|---|
| `FeeBilling.Web` | `System.Web`, Forms auth, Unity, `HttpRuntime.Cache`, Newtonsoft PascalCase contracts | ASP.NET Core APIs behind YARP | Medium | Many routes; the AngularJS app depends on exact JSON shapes |
| `FeeBilling.Core` | Static classes; `DbContextFactory.Current` reads `HttpContext.Current.Items`; `double` money; hard-coded `/4` | Pure `FeeEngine` in `FeeBilling.Domain` | **High** | It produces the invoices |
| `FeeBilling.Data` | EF6 Database-First EDMX, ADO.NET `DataSet`s, 14 stored procedures | EF Core 10 | Medium | Two models over one schema, no owner |
| `FeeBilling.BillingRunner` | Windows Service, `TransactionScope` over two databases (MSDTC), `Parallel.ForEach` on a shared `DbContext` | Worker Service + outbox + Service Bus | **High** | All-or-nothing runs; a crash restarts from zero |
| `FeeBilling.Ingestion.Wcf` | WCF server, `BinaryFormatter`, `Encoding.Default`, culture-dependent parsing | CoreWCF shim + Blob Storage + parser worker | **High** | Frozen SOAP contract; three firms can't upgrade their agent for ~6 months |
| `FeeBilling.Invoicing` | `System.Drawing` (GDI+), Framework-only PDF library | Cross-platform PDF library | Low–Medium | Client-visible output, but not money-calculating |
| `FeeBilling.Web/App` | AngularJS 1.6, end of life since January 2022 | Angular, route-level strangler | Medium | Tied to legacy JSON contracts and `#!` hash routing |

## Demo

```bash
docker compose up -d
dotnet run --project tools/FeeBilling.DbInit

# Legacy: builds anywhere, runs on Windows. Note the test result nobody trusts.
dotnet build legacy/FeeBilling.sln
dotnet test  legacy/FeeBilling.Tests          # Failed 9, Passed 30, Skipped 22

# Inventory: where does legacy touch APIs that don't exist (or behave differently) in modern .NET?
git grep -nE "HttpContext\.Current|ConfigurationManager|BinaryFormatter|TransactionScope|System\.Drawing|Encoding\.Default|HttpRuntime\.Cache|ServiceBase" -- legacy

# Modern: show the strangler working, and the coupling it introduced
dotnet run --project src/FeeBilling.Accounts.Api    # :5101
dotnet run --project src/FeeBilling.Gateway         # :5000
curl -i "http://localhost:5000/api/accounts?firmId=1"   # 500 while the legacy IIS app is not running
```

## Traps to call out

- **Answering with a technology list.** "YARP, CoreWCF, EF Core" is not a strategy. Order, risk reduction and reversibility are.
- **Starting with the fee engine because it's the most important part.** It's the part you can least afford to get wrong, so it moves *after* its behaviour has been captured.
- **Refactoring legacy to make it testable before capturing its output.** You've then changed the thing you're trying to measure.
- **Trusting the handover.** ADR-0002 is out of date; the `decimal(18,2)` rate mapping bug hasn't bitten yet only because nothing writes tiers.
- **Forgetting that writes break rollback.** Routing back to legacy doesn't undo rows the new service already wrote in a shape legacy can't read.
- **Never deleting the legacy path.** `AccountsController` still exists. Until it's removed, a routing mistake silently serves stale behaviour.

## Key terms

Strangler fig · facade / front door · vertical slice · characterization test · parity · expand/contract · shadow run · ADR (Architecture Decision Record) · blast radius

## After the video

1. Write the Day 1 interview artifact: a one-page legacy system map and risk register (the table above, with your own risk ratings and reasons).
2. Draft an ADR that supersedes ADR-0002 and retargets new services to .NET 10 LTS (supported until November 2028).
3. Practise the 30-second answer to "How would you approach modernizing a large .NET Framework application?" out loud.

## References

- `docs/brasswick-modernization-training-plan.md`: Sections 1, 2, 6, 7, 8 and 12.2
- `docs/handover.md`, `docs/adr/0001` to `0004`
- Martin Fowler, *StranglerFigApplication* (bliki)
- Microsoft Learn: *Incremental ASP.NET to ASP.NET Core migration*
