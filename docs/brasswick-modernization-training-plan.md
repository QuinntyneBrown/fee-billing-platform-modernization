# Brasswick Wealth Systems Final Interview Prep: .NET Framework → Modern .NET Modernization

**Role:** Senior .NET Fullstack Developer (Modernization SME, hands-on design + implementation)
**Goal:** Walk into the final interview having *actually done* an incremental Framework → .NET migration on a realistic fee-billing system, with opinions, trade-offs, and war stories to back them up.

---

## Table of Contents

1. [What This Interview Is Really Testing](#1-what-this-interview-is-really-testing)
2. [Target Platform Decisions (Know These Cold)](#2-target-platform-decisions-know-these-cold)
3. [Training Plan (10-Day and 4-Day Tracks)](#3-training-plan)
4. [The Practice Project: FeeBilling Platform](#4-the-practice-project-feebilling-platform)
5. [Domain Primer: Wealth Management Fee Billing](#5-domain-primer-wealth-management-fee-billing)
6. [The Legacy System (As Found)](#6-the-legacy-system-as-found)
7. [Current State: What's Already Modernized](#7-current-state-whats-already-modernized)
8. [Target Architecture](#8-target-architecture)
9. [Work Packages Left for You](#9-work-packages-left-for-you)
10. [Seed Data and Golden-Master Scenarios](#10-seed-data-and-golden-master-scenarios)
11. [Building the Practice Repo](#11-building-the-practice-repo)
12. [Interview Preparation](#12-interview-preparation)
13. [Cheat Sheet: Framework → Modern .NET Mappings](#13-cheat-sheet-framework--modern-net-mappings)

---

## 1. What This Interview Is Really Testing

"SME who can design and implement, hands-on" usually means they want someone who can do three things at once:

| Capability | What they'll probe | What you need to show |
|---|---|---|
| **Migration strategy** | Big-bang vs incremental, sequencing, risk | Strangler fig, parity proof, reversible steps, ADRs |
| **Hands-on depth** | The ugly parts: `System.Web`, WCF, EF6, statics, MSDTC, `BinaryFormatter` | You've hit these problems and know the specific fix for each |
| **Domain safety** | Fee calculations can't be wrong; regulators and clients audit them | Golden-master testing, `decimal` discipline, rounding rules, shadow runs |
| **Enablement** | "SME" = others learn from you | Playbooks, templates, analyzers, pairing, reference implementations |
| **Fullstack** | AngularJS → Angular is almost certainly part of the same modernization | Route-level strangler for the UI, typed API clients |

The strongest single message you can land:

> *"In fee billing, the migration is only done when you can prove the new system produces the same invoice, to the cent, for every account, and explain every place it intentionally doesn't."*

That's your Canadarm3 verification mindset translated directly into their domain.

---

## 2. Target Platform Decisions (Know These Cold)

Verify these dates before the interview, but as of September 2026:

| Version | Type | Support ends | Implication |
|---|---|---|---|
| .NET Framework 4.8 / 4.8.1 | Windows component | Tied to Windows OS lifecycle | Not "dying," but frozen: no new features, no Linux, no containers-that-aren't-Windows |
| .NET 8 | LTS | **November 10, 2026** | If Brasswick migrated anything to .NET 8, it needs an upgrade *now* |
| .NET 9 | STS (extended to 24 months) | November 2026 | Same cliff as .NET 8 |
| .NET 10 | LTS | November 2028 | **The correct target for a migration starting today** |

**Talking point:** "If part of the estate is already on .NET 8, there's a two-month clock. Moving 8 → 10 is cheap compared to 4.8 → 10, so I'd do that first as a quick win and to establish the upgrade pipeline everyone else follows."

**Other decisions to have an opinion on:**

- **Shared libraries:** Retarget to `netstandard2.0` so both the Framework app and the new services can consume them during transition. Drop to `net10.0` only once Framework consumers are gone.
- **Incremental ASP.NET migration:** `Microsoft.AspNetCore.SystemWebAdapters` (shared session, remote authentication, `HttpContext` shim) plus **YARP** as the front door.
- **WCF:** CoreWCF for server-side compatibility when you can't change the clients; REST/gRPC when you can.
- **Tooling:** .NET Upgrade Assistant was Microsoft's tool for this; Microsoft has been steering people toward GitHub Copilot's app modernization tooling. Check current status before citing either. Either way, **tools do the csproj/package grunt work; humans do the architecture.**
- **Licensing gotchas:** MediatR and AutoMapper moved to commercial licences in 2025. In a regulated vendor, you check licences before adding dependencies. A hand-rolled CQRS dispatcher is ~50 lines.

---

## 3. Training Plan

### 10-Day Track (recommended)

Each day has **Learn** (read/watch), **Build** (project work), and **Interview Artifact** (something you can talk to or show).

| Day | Learn | Build | Interview Artifact |
|---|---|---|---|
| **1** | Fee-billing domain (Section 5). .NET Framework vs .NET runtime differences. Strangler fig pattern. | Stand up the repo (Section 11). Build and run legacy. Load seed data. | **Legacy system map + risk register** (1 page) |
| **2** | Characterization testing, golden-master / approval testing (Verify library). | **WP-01:** Golden-master harness. | Parity report format; the "double vs decimal" finding |
| **3** | Pure domain design, strategy pattern, `TimeProvider`, rounding modes, largest-remainder allocation. | **WP-02:** Port the fee engine. | ADR: "Parity first, fix behind a flag" |
| **4** | ASP.NET Core pipeline, ProblemDetails, API versioning, idempotency keys, `WebApplicationFactory`. | **WP-03:** Fee Schedules + Billing API. | OpenAPI spec + integration test suite |
| **5** | EF6 → EF Core differences (lazy loading, `Include` semantics, split queries, no EDMX). Testcontainers. | **WP-06:** Data access migration. | Schema ownership ADR |
| **6** | Worker Services, `BackgroundService`, `IDbContextFactory`, outbox pattern, Azure Service Bus. | **WP-04:** Billing run worker. | Sequence diagram of a resumable billing run |
| **7** | CoreWCF, culture-invariant parsing, `BinaryFormatter` removal. Options pattern, OpenTelemetry. | **WP-05:** Custodian ingestion. **WP-07:** Config/logging/observability. | "Things that broke silently" list |
| **8** | OIDC / Entra ID, policy-based auth, multi-tenant isolation. Angular standalone components, signals. | **WP-08:** Auth. **WP-09:** One AngularJS screen → Angular. | Before/after screen + typed client |
| **9** | Cutover patterns: shadow traffic, parallel runs, feature flags, rollback. | **WP-10:** Cutover and decommission plan. | Cutover runbook |
| **10** | — | Mock interview (Section 12). 10-minute project walkthrough rehearsal. | Polished STAR stories, questions for them |

### 4-Day Compressed Track (if the interview is soon)

| Day | Focus |
|---|---|
| **1** | Domain primer + legacy walkthrough + **WP-01** golden master (this is your best story; don't skip it) |
| **2** | **WP-02** fee engine port + **WP-03** Billing API (one endpoint, fully done, with tests) |
| **3** | **WP-04** worker (skeleton + outbox). For WP-05 through WP-10, write **ADRs only**, no code. |
| **4** | Interview rehearsal: system design prompt, STAR stories, 10-minute walkthrough |

> **Rule for both tracks:** Depth beats breadth. One work package done *properly* (tests, parity proof, ADR) is a better interview story than five half-done ones.

---

## 4. The Practice Project: FeeBilling Platform

**Fictional context:** You've joined a wealth-tech vendor whose flagship product, **FeeBilling**, calculates and collects advisory fees for ~40 wealth management firms (≈ 350,000 accounts). It was built 2011–2016 on .NET Framework and has been maintained, not evolved. Enterprise clients now require Azure-hosted, containerized deployments, SSO with their own identity provider, and SOC 2 evidence the current stack makes painful.

A previous team started an incremental migration and got about 30% of the way through before being reassigned. **You are the modernization SME brought in to finish it and to set the pattern the rest of the engineering team will follow.**

**Repository name:** `fee-billing-platform-modernization`

---

## 5. Domain Primer: Wealth Management Fee Billing

You don't need to be a billing expert, but you need to speak the language. These concepts all appear in the project.

| Term | Meaning |
|---|---|
| **AUM** | Assets Under Management. The base on which most advisory fees are charged. |
| **Billable AUM** | AUM after **exclusions** (e.g., cash sleeve, legacy positions, proprietary funds the firm doesn't bill on). |
| **Fee schedule** | Rules for turning billable AUM into a fee. Assigned to accounts, households, or advisors. |
| **Tiered (marginal)** | Each band of AUM is charged its own rate, like income tax brackets. |
| **Blended / cliff** | The whole AUM is charged at the rate of the highest tier reached. |
| **Flat** | Fixed dollar amount per period regardless of AUM. |
| **Householding** | Linked accounts (a family) combine AUM to reach lower-rate tiers. The household fee is then **allocated** back to each account. |
| **Minimum fee** | Floor on the annual fee, applied per account or per household. |
| **In advance / in arrears** | Bill at the start of the period on opening AUM, or at the end on closing (or average daily) AUM. |
| **Flow pro-rating** | Large deposits/withdrawals mid-period adjust the fee by days remaining. |
| **Day-count basis** | Quarterly fee = annual × (days in period ÷ 365) vs annual ÷ 4. **This difference is real money.** |
| **Custodian** | The institution holding the assets (e.g., Schwab, Fidelity, Pershing, NBIN in Canada). Sends daily position and transaction files. |
| **Fee debit / collection** | Instruction sent to the custodian to withdraw the fee from the client account. |
| **Reconciliation** | Matching what was billed against what the custodian actually debited. |

### Worked Example 1: Tiered fee and day-count basis

Schedule **STD-TIERED**: 1.00% on the first $1M, 0.75% on the next $4M, 0.50% above $5M. Minimum annual fee: $1,000.

Account AUM on Sept 30, 2026: **$2,500,000.** Period: Q3 2026 (Jul 1 – Sep 30 = **92 days**).

```
Annual fee = 1,000,000 × 1.00%  = 10,000.00
           + 1,500,000 × 0.75%  = 11,250.00
                                  ---------
                                  21,250.00

Actual/365:  21,250.00 × 92 / 365 = 5,356.164…  → 5,356.16
Annual/4:    21,250.00 / 4         =              5,312.50
                                                  --------
Difference per account per quarter:                  43.66
```

Legacy uses `/4`. The fee agreement for some firms says actual/365. **Which one is correct is a business question, not a technical one.** Your migration must reproduce legacy exactly *and* make the basis configurable per firm.

### Worked Example 2: Household allocation and the missing penny

Household H-100 has three accounts: A $1.5M, B $1.0M, C $0.5M (total $3M).

```
Household annual fee = 10,000 + 2,000,000 × 0.75% = 25,000.00
Q3 (actual/365)      = 25,000 × 92 / 365          = 6,301.37 (rounded)

Allocate by AUM share (A 50%, B 33.33%, C 16.67%) with AwayFromZero rounding:
  A: 6,301.37 × 0.5     = 3,150.685   → 3,150.69
  B: 6,301.37 / 3       = 2,100.4567  → 2,100.46
  C: 6,301.37 / 6       = 1,050.2283  → 1,050.23
                                        --------
  Sum                                   6,301.38   ← one cent more than the household fee
```

Note that `Math.Round` defaults to **banker's rounding** (`ToEven`) in *both* .NET Framework and modern .NET, which would give A 3,150.68. Legacy explicitly uses `AwayFromZero` here. Someone "cleaning up" that parameter changes invoices.

**Correct approach: largest-remainder allocation.** Floor each share to the cent (3,150.68 + 2,100.45 + 1,050.22 = 6,301.35), then hand out the 2 leftover cents to the accounts with the largest fractional remainders (C: .8333, B: .6667). Result: 3,150.68 / 2,100.46 / 1,050.23 = 6,301.37. ✔

---

## 6. The Legacy System (As Found)

### 6.1 Solution structure

```
FeeBilling.sln   (.NET Framework 4.7.2, old-style csproj, packages.config)
│
├── FeeBilling.Web                ASP.NET MVC 5 + Web API 2, hosted in IIS
│   ├── Controllers/Api/          AccountsController, FeeSchedulesController,
│   │                             BillingController, InvoicesController, HouseholdsController
│   ├── Controllers/Mvc/          HomeController (serves AngularJS shell)
│   ├── App/                      AngularJS 1.6 SPA (billing review, schedules, invoices)
│   ├── Global.asax               Unity container, log4net, route registration
│   └── Web.config                + Web.Debug/Release/ClientX.config transforms
│
├── FeeBilling.Core               Business logic: static FeeCalculator, AumService,
│                                 HouseholdAllocator, BillingRunService
│
├── FeeBilling.Data               EF6 Database-First (FeeBilling.edmx), 14 stored procs,
│                                 some ADO.NET DataSets for invoice exports
│
├── FeeBilling.Ingestion.Wcf      WCF service (CustodianFeedService.svc, basicHttpBinding)
│                                 Receives files pushed by an on-prem "feed agent"
│
├── FeeBilling.BillingRunner      Windows Service. Polls BillingRunQueue table every 30s.
│
├── FeeBilling.Invoicing          PDF invoice generation via System.Drawing + a
│                                 commercial PDF lib (v4, Framework-only)
│
└── FeeBilling.Tests              MSTest. 61 tests, 9 failing, 22 ignored. Nobody trusts it.
```

### 6.2 Technology inventory

| Concern | Legacy | Problem in modern .NET |
|---|---|---|
| Web framework | MVC 5 + Web API 2 on `System.Web` | `System.Web` doesn't exist |
| DI | Unity 5 | Replace with `Microsoft.Extensions.DependencyInjection` |
| Data | EF6 Database-First, EDMX | No EDMX in EF Core; lazy loading off by default |
| Transactions | `TransactionScope` across Billing + Reporting DBs → **MSDTC** | Distributed transactions only on Windows (.NET 7+); not viable in Linux containers |
| Caching | `HttpRuntime.Cache` | Gone; use `IMemoryCache` / `HybridCache` |
| Config | `Web.config` + transforms + `ConfigurationManager` | `appsettings.json` + options + Key Vault / App Configuration |
| Logging | log4net, string concatenation | `ILogger<T>` + structured logging + OpenTelemetry |
| Auth | Forms authentication, custom membership tables, `machineKey` cookie | OIDC (Entra ID), cookie sharing during transition |
| Background jobs | Windows Service + `System.Timers.Timer` | Worker Service / container job |
| Ingestion | WCF `basicHttpBinding` | No server WCF in .NET; CoreWCF or new API |
| Serialization | `BinaryFormatter` for staged batches in `varbinary(max)` | **Removed in .NET 9** |
| PDF | `System.Drawing` + Framework-only PDF lib | `System.Drawing.Common` is Windows-only |
| Frontend | AngularJS 1.6, Bootstrap 3, jQuery | AngularJS EOL since Jan 2022 |

### 6.3 Code excerpts (the smells you must fix)

**`FeeBilling.Core/FeeCalculator.cs`**

```csharp
public static class FeeCalculator
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(FeeCalculator));

    public static decimal CalculateQuarterlyFee(int accountId, DateTime periodEnd)
    {
        var db = DbContextFactory.Current; // pulled from HttpContext.Current.Items
        var account = db.Accounts
            .Include("FeeSchedule.Tiers")
            .Single(a => a.Id == accountId);

        var cacheKey = "aum_" + accountId + "_" + periodEnd.ToShortDateString(); // culture-dependent key
        var aum = HttpRuntime.Cache[cacheKey] as double?;
        if (aum == null)
        {
            aum = (double)AumService.GetBillableAum(accountId, periodEnd);
            HttpRuntime.Cache.Insert(cacheKey, aum, null,
                DateTime.Now.AddMinutes(30), Cache.NoSlidingExpiration); // local time
        }

        double fee = 0;
        double remaining = aum.Value;
        foreach (var tier in account.FeeSchedule.Tiers.OrderBy(t => t.LowerBound))
        {
            var span = tier.UpperBound.HasValue
                ? Math.Min(remaining, (double)(tier.UpperBound.Value - tier.LowerBound))
                : remaining;
            fee += Math.Round(span * (double)tier.AnnualRate / 4, 2);  // double + per-tier rounding + /4
            remaining -= span;
            if (remaining <= 0) break;
        }

        var minQuarterly = (double)account.FeeSchedule.MinimumAnnualFee / 4;
        if (fee < minQuarterly) fee = minQuarterly;

        Log.Info("Fee for " + accountId + " = " + fee);
        return (decimal)fee;
    }
}
```

Count the problems: static class (untestable), hidden dependency on `HttpContext` (breaks in the Windows Service, which has a hack to fake it), `double` for money, per-tier rounding, hard-coded `/4`, culture-dependent cache key, `DateTime.Now`, only supports tiered schedules (blended is a separate copy-pasted class), no householding (handled elsewhere, see below).

**`FeeBilling.Core/HouseholdAllocator.cs`**

```csharp
public static Dictionary<int, decimal> Allocate(decimal householdFee, IList<AccountAum> members)
{
    var total = members.Sum(m => m.BillableAum);
    return members.ToDictionary(
        m => m.AccountId,
        m => Math.Round(householdFee * m.BillableAum / total, 2, MidpointRounding.AwayFromZero));
    // Sum of allocations can differ from householdFee by ±0.01 × (n-1). Divide-by-zero if total == 0.
}
```

**`FeeBilling.Web/Controllers/Api/BillingController.cs`**

```csharp
[RoutePrefix("api/billing")]
public class BillingController : ApiController
{
    [HttpPost, Route("runs")]
    public IHttpActionResult StartRun(BillingRunRequest req)
    {
        var user = HttpContext.Current.User.Identity.Name;
        var runId = BillingRunService.Enqueue(req.FirmId, req.PeriodEnd, user);
        return Ok(new { runId });   // double-click = two billing runs = double-billed clients
    }

    [HttpGet, Route("runs/{id:int}/invoices")]
    public HttpResponseMessage GetInvoices(int id, string format = "json")
    {
        DataSet ds = InvoiceRepository.GetInvoiceDataSet(id);   // ADO.NET, no paging
        if (format == "csv") return CsvResult(ds.Tables[0]);
        return Request.CreateResponse(HttpStatusCode.OK, ds);
    }
}
```

**`FeeBilling.BillingRunner/BillingRunnerService.cs`**

```csharp
protected override void OnStart(string[] args)
{
    HttpContext.Current = FakeHttpContext.Create();   // so FeeCalculator works outside IIS
    _timer = new Timer(30_000);
    _timer.Elapsed += (s, e) => ProcessQueue();
    _timer.Start();
}

private void ProcessQueue()
{
    var run = _db.BillingRunQueue.FirstOrDefault(r => r.Status == "Pending");
    if (run == null) return;

    var accounts = _db.Accounts.Where(a => a.FirmId == run.FirmId).ToList();
    using (var scope = new TransactionScope())          // Billing DB + Reporting DB → MSDTC
    {
        Parallel.ForEach(accounts, acct =>              // shared DbContext across threads
        {
            var fee = FeeCalculator.CalculateQuarterlyFee(acct.Id, run.PeriodEnd);
            _db.Invoices.Add(new Invoice { AccountId = acct.Id, Amount = fee, RunId = run.Id });
            _reportingDb.FeeFacts.Add(new FeeFact { ... });
        });
        _db.SaveChanges();
        _reportingDb.SaveChanges();
        scope.Complete();
    }
    run.Status = "Complete";   // crash at account 40,000 = whole run rolls back, starts over
    _db.SaveChanges();
}
```

**`FeeBilling.Ingestion.Wcf/CustodianFeedService.svc.cs`**

```csharp
public FeedReceipt SubmitPositionFile(string custodianCode, string fileName, byte[] content)
{
    var lines = Encoding.Default.GetString(content).Split('\n');   // Encoding.Default differs on .NET Core
    var positions = lines.Skip(1).Select(l => new Position
    {
        AccountNumber = l.Substring(0, 12).Trim(),
        MarketValue   = decimal.Parse(l.Substring(40, 18)),        // culture-dependent; fr-CA servers break
        AsOfDate      = DateTime.Parse(l.Substring(58, 10))        // same
    }).ToList();

    var formatter = new BinaryFormatter();                         // removed in .NET 9
    using (var ms = new MemoryStream())
    {
        formatter.Serialize(ms, positions);
        _db.StagedBatches.Add(new StagedBatch { Payload = ms.ToArray(), Status = "Received" });
    }
    _db.SaveChanges();
    return new FeedReceipt { Accepted = positions.Count };
}
```

---

## 7. Current State: What's Already Modernized

The previous team chose a **strangler fig** approach. Here's what exists when you arrive:

```
fee-billing-platform-modernization/
│
├── legacy/                               (unchanged except where noted)
│   └── FeeBilling.sln
│
├── src/
│   ├── FeeBilling.Domain/                ✅ netstandard2.0, SDK-style. Entities + value objects
│   │                                        extracted from Core. Consumed by BOTH legacy and new.
│   ├── FeeBilling.Gateway/               ✅ .NET 10 + YARP. Front door for all traffic.
│   ├── FeeBilling.Accounts.Api/          ✅ .NET 10. Accounts + Households endpoints migrated.
│   ├── FeeBilling.Infrastructure/        🟡 EF Core 10. Accounts mapped. FeeSchedule entities
│   │                                        scaffolded but NOT wired; Tiers mapping is wrong.
│   └── FeeBilling.ServiceDefaults/       ✅ Health checks, OpenTelemetry stub (no exporter yet)
│
├── tests/
│   ├── FeeBilling.Accounts.Api.Tests/    ✅ xUnit + WebApplicationFactory + Testcontainers (SQL Server)
│   └── FeeBilling.Domain.Tests/          🟡 12 tests, value objects only
│
├── docs/adr/
│   ├── 0001-strangler-fig-migration.md   ✅
│   ├── 0002-target-dotnet-version.md     ⚠️ Says ".NET 8 LTS". Out of date. Fix it.
│   ├── 0003-yarp-gateway.md              ✅
│   └── 0004-shared-database-during-transition.md  ✅
│
├── .github/workflows/ci.yml              ✅ legacy builds on windows-latest, modern on ubuntu-latest
└── Directory.Build.props                 ✅ Nullable, TreatWarningsAsErrors, central package mgmt
```

### Gateway routing (current)

```json
"ReverseProxy": {
  "Routes": {
    "accounts":   { "ClusterId": "modern-accounts", "Match": { "Path": "/api/accounts/{**rest}" } },
    "households": { "ClusterId": "modern-accounts", "Match": { "Path": "/api/households/{**rest}" } },
    "fallback":   { "ClusterId": "legacy-iis",      "Match": { "Path": "{**catch-all}" }, "Order": 1000 }
  }
}
```

### Auth during transition (current)

The legacy app still owns login (Forms auth). New services use **`Microsoft.AspNetCore.SystemWebAdapters` remote authentication**: when a request arrives at the new API, it calls back to the legacy app to resolve the user. It works, but adds a round-trip per request and couples every new service to legacy uptime.

### Known issues the previous team left in `docs/handover.md`

1. `ADR-0002` targets .NET 8; support ends November 10, 2026.
2. `FeeSchedule.Tiers` EF Core mapping uses `decimal(18,2)` for `AnnualRate`. The DB column is `decimal(9,6)`. Rates like 0.0075 would truncate to 0.01 on write. **Nothing writes yet, so nobody noticed.**
3. The Accounts API returns `DateTime` with `Kind=Unspecified`; the AngularJS app interprets them as local time. Dates off by one for users west of UTC after 8 p.m.
4. Both the EDMX and EF Core model describe the same tables. No one decided who owns schema changes.
5. No parity tests exist for anything fee-related. "We were going to do that next."

---

## 8. Target Architecture

```
                         ┌────────────────────────────┐
     Browser ──────────▶ │  FeeBilling.Gateway (YARP) │ ◀── Entra ID / client IdP (OIDC)
                         └─────────────┬──────────────┘
          ┌──────────────────┬─────────┴─────────┬──────────────────────┐
          ▼                  ▼                   ▼                      ▼
   Angular SPA        Accounts.Api        Billing.Api            legacy IIS (shrinking)
   (static host)      (.NET 10)           (.NET 10)              .NET Framework 4.7.2
                                               │
                                     outbox ──▶│──▶ Azure Service Bus ──▶ Billing.Worker (.NET 10)
                                               │                              │
                                               ▼                              ▼
                                    ┌─────────────────────┐       FeeBilling.Domain (pure)
                                    │ SQL (Billing DB)    │        - FeeEngine
                                    │ Reporting via CDC / │        - Strategies: Tiered, Blended,
                                    │ events, not MSDTC   │          Flat, Householded
                                    └─────────────────────┘        - Allocation (largest remainder)
                                               ▲
   Custodian files ──▶ Blob Storage ──▶ Ingestion.Worker (.NET 10)
   Legacy feed agent ──▶ Ingestion.CoreWcf (compat shim, to be retired)
```

### Architectural principles (put these in ADR-0005)

1. **The domain is pure.** `FeeBilling.Domain` has no references to EF, ASP.NET, logging frameworks, or the clock. Inputs in, invoices out. This is what makes parity testing possible.
2. **Money is `decimal`, rounding is explicit.** Every `Math.Round` call specifies `MidpointRounding`. Rounding happens at defined points only (end of calculation, allocation), never mid-calculation, unless parity mode requires it.
3. **Every billing run is idempotent and resumable.** Keyed by `(FirmId, PeriodEnd, RunType)`. Accounts are processed in chunks; completed chunks are never redone.
4. **No distributed transactions.** Transactional outbox for anything that must leave the Billing DB.
5. **Legacy behaviour is reproduced first, fixed second.** Fixes ship behind per-firm feature flags with business sign-off.
6. **Every calculation is explainable.** Each invoice line stores a calculation trace (tiers applied, AUM used, basis, rounding) for audit.

---

## 9. Work Packages Left for You

Each work package has **Context**, **Tasks**, **Acceptance Criteria**, and **Traps** (the things that make you sound experienced in the interview).

### WP-01: Golden-Master Parity Harness ⭐ *Do this first*

**Context:** You can't safely change a fee engine without proof of current behaviour. The legacy tests are untrustworthy.

**Tasks**
- Build `tools/FeeBilling.ParityRunner.Legacy` (net472 console) that references `legacy/FeeBilling.Core`, loads seed data (Section 10), and writes `golden/q3-2026.csv`: `AccountId, ScheduleCode, BillableAum, Fee, HouseholdId, AllocatedFee`.
- Build `tests/FeeBilling.Parity.Tests` (net10.0) that runs the new engine on the same inputs and diffs against the golden file.
- Output a **parity report**: exact matches, differences with amount and category (e.g., `DOUBLE_PRECISION`, `ROUNDING_MODE`, `DAY_COUNT`, `ALLOCATION_PENNY`).
- Commit golden files. Treat any change to them as a reviewed, deliberate act.

**Acceptance Criteria**
- 100% of seed scenarios produce a golden record.
- Parity test fails CI on any unexplained difference.
- A "known differences" allowlist exists, each entry linked to an ADR or ticket.

**Traps**
- The legacy engine depends on `HttpContext.Current`. Your console runner needs the same `FakeHttpContext` hack the Windows Service uses, or a thin seam. **Do not refactor legacy code to make it testable before you've captured its output.** That's the whole point.
- Use `CultureInfo.InvariantCulture` when writing the CSV, or your golden file changes when someone runs it on a `fr-CA` laptop.

---

### WP-02: Port the Fee Engine to a Pure Domain

**Context:** Replace `FeeCalculator`, `BlendedFeeCalculator`, and `HouseholdAllocator` statics with a testable engine.

**Tasks**
- Design:
  ```csharp
  public interface IFeeStrategy
  {
      FeeScheduleType Handles { get; }
      AnnualFeeResult CalculateAnnual(Money billableAum, FeeSchedule schedule);
  }

  public sealed class FeeEngine(IEnumerable<IFeeStrategy> strategies, BillingPolicy policy)
  {
      public FeeResult Calculate(FeeRequest request);   // pure, no I/O, no clock
  }

  public sealed record BillingPolicy(
      DayCountBasis Basis,                // AnnualDividedByFour | Actual365
      RoundingMode Rounding,              // LegacyPerTier | EndOfCalculation
      MidpointRounding Midpoint,
      AllocationMethod Allocation);       // LegacyProportional | LargestRemainder
  ```
- `Money` value object wrapping `decimal` with currency.
- Implement Tiered, Blended, Flat, Householded strategies. Minimum fee applied at the correct level (account vs household).
- `BillingPolicy.Legacy` preset reproduces legacy exactly, **including the `double` behaviour** (a `LegacyDoubleArithmetic` flag that deliberately converts through `double`; document why it exists and when it will be removed).
- `BillingPolicy.Corrected` preset uses decimal throughout, actual/365, end-of-calculation rounding, largest-remainder allocation.
- Produce a calculation trace for every result.

**Acceptance Criteria**
- With `BillingPolicy.Legacy`: **0 differences** against the golden master.
- With `BillingPolicy.Corrected`: all differences categorized in the parity report with total dollar impact per firm.
- ≥ 95% branch coverage on `FeeBilling.Domain`. Property-based tests (FsCheck or CsCheck) for: allocation always sums to household fee; fee is monotonic non-decreasing in AUM; fee ≥ minimum.

**Traps**
- Reproducing a bug on purpose feels wrong. It's correct. Explain: *"You can't change billing behaviour for 350,000 accounts as a side effect of a framework upgrade. The migration and the fix are two separate, separately approved changes."*
- Zero-AUM household → legacy divides by zero. Decide and document behaviour.

---

### WP-03: Fee Schedules & Billing API (ASP.NET Core)

**Context:** Migrate `FeeSchedulesController`, `BillingController`, `InvoicesController`.

**Tasks**
- Vertical slices with CQRS: `CreateFeeSchedule`, `GetFeeSchedule`, `StartBillingRun`, `GetBillingRun`, `ListInvoices`, `PreviewFee` (what-if calculation, no persistence).
- `POST /api/billing/runs` requires an `Idempotency-Key` header; duplicate keys return the original run (the double-click bug).
- Validation with FluentValidation (or hand-rolled); errors as RFC 9457 `ProblemDetails`.
- API versioning (`/api/v1/...`). **Keep legacy routes working through the gateway**: either route aliases or a YARP transform.
- Invoices: cursor-based paging; CSV export streamed with `IAsyncEnumerable`, not a `DataSet`.
- Contract tests: capture legacy JSON responses and assert the new API's shape matches for endpoints the AngularJS app still calls.
- Update YARP routes to send these paths to `Billing.Api`.

**Acceptance Criteria**
- Integration tests via `WebApplicationFactory` + Testcontainers for every endpoint.
- Legacy AngularJS screens still work unmodified against the new endpoints.
- Two concurrent `POST /runs` with the same key produce exactly one run (test it).

**Traps**
- Web API 2 serialized with Newtonsoft (PascalCase by default in many configs); ASP.NET Core defaults to `System.Text.Json` camelCase. The AngularJS app breaks silently on `undefined` properties. Either configure the serializer to match or write the contract tests first.
- Legacy returned `DataSet` JSON (`{ "Table": [...] }`). Some consumer somewhere depends on that shape. Find out before changing it.

---

### WP-04: Billing Run Worker

**Context:** Replace the Windows Service. This is the highest-risk component: it produces the invoices.

**Tasks**
- `FeeBilling.Billing.Worker` (.NET 10 Worker Service) consuming `BillingRunRequested` messages from Azure Service Bus (use the emulator locally, or an in-memory transport behind an interface).
- `Billing.Api` writes the run + an outbox row in one local transaction; an outbox dispatcher publishes the message.
- Worker splits accounts into chunks (e.g., 500). Each chunk: load inputs → run `FeeEngine` → write invoices + calculation traces → mark chunk complete. **One local transaction per chunk.**
- Resumability: on restart, skip completed chunks.
- Concurrency via `IDbContextFactory<T>` (one context per chunk), bounded parallelism (`Parallel.ForEachAsync` with `MaxDegreeOfParallelism`), never a shared context.
- Reporting DB updated via `InvoicesGenerated` events (or CDC), **not** `TransactionScope`.
- **Shadow mode:** worker can run in `Shadow` mode, writing to `ShadowInvoices` while legacy remains the system of record. Nightly job diffs shadow vs legacy.

**Acceptance Criteria**
- Kill the worker mid-run; restart; run completes with no duplicate or missing invoices (automated test).
- 50,000-account run completes in under 10 minutes locally (measure it; report the number).
- Shadow diff for seed data = 0 with `BillingPolicy.Legacy`.

**Traps**
- `Parallel.ForEach` + shared `DbContext` in legacy "works" because EF6 sometimes doesn't throw. EF Core will throw `InvalidOperationException` on concurrent use. Good; the bug was always there.
- Message delivery is at-least-once. The chunk handler must be idempotent.

---

### WP-05: Custodian Ingestion

**Context:** The on-prem feed agent at 3 client firms can't be updated for ~6 months. It speaks SOAP.

**Tasks**
- `FeeBilling.Ingestion.CoreWcf`: CoreWCF host exposing the **same** `CustodianFeedService` contract so existing agents work unchanged. It writes the file to Blob Storage and returns.
- `FeeBilling.Ingestion.Worker`: blob-triggered parser. Culture-invariant parsing (`CultureInfo.InvariantCulture`, `DateTime.ParseExact`), explicit encoding.
- Parser framework: one parser per custodian format, with a fixed-width field spec, not `Substring` magic numbers.
- Replace `BinaryFormatter` staging with JSON (`System.Text.Json`) staging.
- **One-time migration tool** (net472, because only Framework can still deserialize `BinaryFormatter` payloads safely here): read existing `StagedBatches` rows, re-serialize to JSON.
- Quarantine flow for malformed lines with reason codes, not a failed batch.

**Acceptance Criteria**
- Legacy SOAP client test passes against the CoreWCF host.
- Same position file produces identical parsed output on `en-CA` and `fr-CA` cultures (test both).
- Zero `BinaryFormatter` references in `src/`. Add a CI check.

**Traps**
- `Encoding.Default` is the system ANSI code page on Framework but **UTF-8** on .NET Core. Custodian files with accented names (Québec clients) will garble.
- `BinaryFormatter` deserialization of untrusted data is a security vulnerability. That's a good security talking point.

---

### WP-06: Data Access (EF6 → EF Core)

**Tasks**
- Decide schema ownership. Recommendation: **EF Core migrations become the source of truth**, baselined with an empty initial migration against the existing schema. EDMX becomes read-only; schema changes must be backward-compatible with legacy until it's retired (expand/contract).
- Fix the `decimal(18,2)` rate mapping bug (handover issue #2). Add a test that round-trips a 0.0075 rate.
- Audit every EF6 query that relied on lazy loading. Replace with explicit `Include` or projections.
- Stored procedures: keep the 3 performance-critical ones (call via `FromSql`); port the other 11 to LINQ with tests.
- `DateTime` → `DateTimeOffset` or UTC-kind conventions (handover issue #3).
- Add a Roslyn analyzer or architecture test (NetArchTest) that fails if `FeeBilling.Domain` references EF Core.

**Traps**
- EF Core translates some LINQ differently (e.g., client evaluation was silently allowed in EF Core 2.x and EF6 had quirks; EF Core 3+ throws). Queries that "worked" may now throw at runtime. Integration tests catch this; unit tests with an in-memory provider do not.

---

### WP-07: Configuration, Logging, Observability

**Tasks**
- Map every `Web.config` `appSettings` key and per-client transform to strongly typed options (`IOptions<BillingOptions>` with `ValidateDataAnnotations().ValidateOnStart()`).
- Secrets → Key Vault (or user-secrets locally). Per-client config → Azure App Configuration with labels.
- log4net → `ILogger<T>` with structured templates: `"Fee calculated for {AccountId}: {Fee}"`.
- OpenTelemetry: traces across Gateway → API → Service Bus → Worker; metrics for run duration, accounts/sec, parity diffs.
- Correlation ID propagated from gateway to legacy (header) so a single request is traceable across old and new.

**Traps**
- Logging PII (account numbers, client names) in structured logs means they're now **indexed and searchable**. Add redaction.

---

### WP-08: Authentication & Authorization

**Tasks**
- Introduce OIDC (Entra ID; design for per-client IdP federation). Gateway handles auth; APIs validate tokens.
- Transition: legacy app accepts the new auth cookie or token via SystemWebAdapters shared auth, so users log in once.
- Policy-based authorization: `CanStartBillingRun`, `CanApproveInvoices`, `CanEditFeeSchedules`.
- **Tenant isolation:** `FirmId` claim enforced via EF Core global query filters, plus a test that a user from Firm A can't read Firm B's invoices through any endpoint.
- Audit log: who changed which fee schedule, before/after values.

**Traps**
- Global query filters are easy to bypass with `IgnoreQueryFilters()` or raw SQL. Add an architecture test or analyzer.

---

### WP-09: Frontend (AngularJS → Angular)

**Tasks**
- Choose the approach and write it up as an ADR:
  - **ngUpgrade hybrid** (both frameworks in one bundle), or
  - **Route-level strangler** (new Angular app behind the gateway; migrated routes served by it, the rest by the AngularJS shell). **Recommended here:** simpler, independent deploys, no hybrid-bootstrap complexity.
- Migrate the **Billing Run Review** screen: run status, per-account fee list with virtual scrolling, drill-down to calculation trace, approve action.
- Current Angular: standalone components, signals, new control flow (`@if`, `@for`), `HttpClient` with a typed client generated from the OpenAPI spec (NSwag or openapi-generator).
- Shared auth: SPA uses the gateway's cookie (BFF pattern), no tokens in the browser.

**Acceptance Criteria**
- Screen renders 50,000 rows smoothly.
- Playwright E2E: start run → watch progress → review → approve.

---

### WP-10: Cutover and Decommission

**Tasks** (document, don't necessarily build)
- **Parallel run:** one full quarter in shadow mode for all firms. Exit criterion: zero unexplained differences for two consecutive monthly runs.
- **Firm-by-firm cutover** via feature flag (`BillingEngine = Legacy | Modern`), starting with the smallest, least complex firm.
- **Rollback plan:** legacy remains runnable for one full billing cycle after each firm's cutover. Define exactly what "roll back" means for invoices already sent.
- **Decommission checklist:** remove YARP fallback route, retire CoreWCF shim once agents are upgraded, archive EDMX, drop MSDTC config, shut down IIS, remove legacy CI job.
- **Comms:** what client firms need to know (e.g., if they opt into `BillingPolicy.Corrected`, their clients' fees change by up to $X).

---

## 10. Seed Data and Golden-Master Scenarios

Schedules (all amounts CAD):

| Code | Type | Definition | Min annual |
|---|---|---|---|
| `STD-TIERED` | Tiered | 1.00% ≤ $1M; 0.75% $1M–$5M; 0.50% > $5M | $1,000 |
| `STD-BLENDED` | Blended | Same bands, whole AUM at highest rate reached | $1,000 |
| `FLAT-500` | Flat | $2,000/year | n/a |
| `HH-TIERED` | Householded tiered | `STD-TIERED` applied to combined household AUM | $2,500 (household) |

Scenarios every implementation must cover (Q3 2026, period end Sep 30, 92 days):

| # | Scenario | Setup | Legacy (÷4) | Actual/365 | Why it matters |
|---|---|---|---|---|---|
| S1 | Mid-tier | $2,500,000, `STD-TIERED` | 5,312.50 | 5,356.16 | Day-count basis |
| S2 | Exactly on boundary | $1,000,000, `STD-TIERED` | 2,500.00 | 2,520.55 | Off-by-one in `<` vs `<=` |
| S3 | Minimum fee | $80,000, `STD-TIERED` | 250.00 | 252.05 | Minimum applied after basis |
| S4 | Blended | $2,500,000, `STD-BLENDED` | 4,687.50 | 4,726.03 | Strategy selection |
| S5 | Flat | Any AUM, `FLAT-500` | 500.00 | 504.11 | Does basis apply to flat fees? (Business decision!) |
| S6 | Cash exclusion | $1,200,000 incl. $200,000 cash sleeve | 2,500.00 | 2,520.55 | Exclusions before tiering |
| S7 | Household | H-100: A $1.5M, B $1.0M, C $0.5M | see §5 | 6,301.37 total | Allocation penny |
| S8 | Zero-AUM household | All members $0 | throws | defined | Divide by zero |
| S9 | Opened mid-quarter | Opened Aug 15 (47 days), $1,000,000 | legacy-specific | pro-rated | Partial periods |
| S10 | Large withdrawal | $3M on Jul 1, −$1.5M on Aug 31 | legacy-specific | flow-adjusted | Flow pro-rating |
| S11 | Precision stress | 500 generated accounts, AUM with fractional cents | captured | captured | `double` drift shows up as 1¢ diffs in a handful of accounts |
| S12 | French-Canadian file | Position file with `1 234 567,89` style values and accented names | captured | captured | Culture + encoding |

> Blank "legacy-specific" cells are intentional: you don't compute them, **the golden master captures them**. That's the point of characterization testing: legacy output is the specification, not your opinion of what it should be.

> Double-check the Actual/365 numbers yourself when you implement. Recomputing expected values by hand is a good habit to talk about in the interview.

---

## 11. Building the Practice Repo

### Environment

| Need | Option |
|---|---|
| Run legacy .NET Framework (IIS, WCF, Windows Service) | Windows machine, Windows VM, or Microsoft Dev Box. GitHub Actions `windows-latest` for CI. |
| Build legacy libs on macOS/Linux | SDK-style `net472` projects with `Microsoft.NETFramework.ReferenceAssemblies` compile anywhere; running them needs Windows (or Mono for simple console tools). |
| SQL Server | `mcr.microsoft.com/mssql/server` Docker image |
| Service Bus | Azure Service Bus emulator (Docker) or an in-memory transport behind an interface |
| Blob storage | Azurite (Docker) |

**Pragmatic shortcut:** If you don't want to fight Windows tooling, keep the *legacy web app* as reading material only, but make `FeeBilling.Core` + `ParityRunner.Legacy` actually build and run (net472 console). That's the part that produces the golden master, which is what really matters.

### Generating the scaffold

This document is detailed enough to hand to Claude Code as a spec. A prompt that works well:

```
Using brasswick-modernization-training-plan.md as the specification, generate the
repository described in Sections 6 and 7 ONLY: the legacy solution exactly as described
in Section 6 (including every listed code smell, deliberately), and the partially
modernized state in Section 7 (including the documented bugs in docs/handover.md).
Do NOT implement any work package in Section 9. Include seed data SQL for Section 10
and a docker-compose.yml for SQL Server, Azurite, and the Service Bus emulator.
```

Then do the work packages yourself. **The value is in you doing WP-01 through WP-04 by hand.**

### Suggested commit discipline

- One branch per work package; PR description written as if a teammate will review it.
- Every WP ends with an ADR (`docs/adr/00NN-*.md`) and an updated parity report.
- These PRs are your portfolio. You can reference them in the interview ("here's how I'd structure that PR").

---

## 12. Interview Preparation

### 12.1 Your 10-minute project walkthrough

Practice this out loud until it fits in 10 minutes.

1. **(1 min) Context:** Fee billing for 350k accounts, Framework 4.7.2, a stalled strangler-fig migration.
2. **(2 min) First move: parity before code.** Golden master from legacy. Show the parity report and the categories of difference found.
3. **(2 min) Fee engine:** pure domain, `BillingPolicy`, reproducing legacy bugs deliberately, fixes behind flags. The penny example.
4. **(2 min) Billing worker:** chunked, idempotent, resumable, outbox instead of MSDTC, shadow mode.
5. **(1 min) The silent killers:** `Encoding.Default`, `BinaryFormatter`, Newtonsoft → STJ casing, rate column precision, culture parsing.
6. **(1 min) Cutover:** a quarter of shadow runs, firm-by-firm flags, rollback defined.
7. **(1 min) Enablement:** ADR templates, a reference vertical slice, an analyzer that flags `HttpContext.Current` / `ConfigurationManager` / `BinaryFormatter` usage, and a migration playbook other devs follow.

### 12.2 Likely questions and answer sketches

**"How would you approach modernizing a large .NET Framework application?"**
Inventory first (dependencies, `System.Web` coupling, WCF, background jobs, Framework-only packages). Retarget shared libraries to `netstandard2.0`. YARP in front, migrate route by route with SystemWebAdapters for session/auth. Highest-risk logic gets characterization tests *before* it moves. Each step is independently deployable and reversible. Never a big bang for a system that bills money.

**"Big bang or incremental?"**
Incremental, unless the app is small and has good tests. Big bang means the business gets zero value until the end and you find the hard problems all at once. The cost of incremental is running two stacks and a shared database for a while; worth it.

**"How do you know the new system is correct?"**
Three layers: golden-master tests on the engine, contract tests on the APIs, shadow runs in production against real data for a full billing cycle. Every difference is either fixed or explicitly accepted with sign-off.

**"You find a bug in the legacy fee calculation during migration. What do you do?"**
Reproduce it in the new system first (parity), raise it separately with product/compliance, quantify impact per firm, fix behind a flag with sign-off. Explain why: migration changes and behaviour changes must be separable, or you can't tell which one caused a client's invoice to change.

**"How do you handle WCF?"**
CoreWCF when you can't change clients (external agents, partners); rewrite to REST/gRPC when you can. Use the CoreWCF shim as a temporary adapter with a retirement date.

**"What breaks silently when moving to modern .NET?"**
`Encoding.Default` → UTF-8. `BinaryFormatter` gone. JSON casing and serializer behaviour. EF Core no lazy loading, and query translation differences. Culture/globalization behaviour (ICU vs NLS on Windows since .NET 5). `System.Drawing` Windows-only. `TransactionScope` distributed transactions. `AppDomain` and remoting gone.

**"How would you make other developers productive on the migration?"** (the SME question)
Reference implementation of one vertical slice, end to end. ADR template and decision log. Roslyn analyzer or scripted checks that find legacy APIs. A migration playbook with the traps list. Pairing on the first slice for each dev, then reviewing. Make the right way the easy way.

**"Why .NET 10 and not .NET 8?"**
.NET 8 support ends November 10, 2026. .NET 10 is LTS through November 2028. Starting a migration on .NET 8 today means a second migration within months.

### 12.3 System design rehearsal prompt

> *"Design the quarterly billing run for 500,000 accounts across 60 firms. It must finish overnight, be resumable, be auditable, and never double-bill."*

Hit these points: partition by firm then chunk; idempotency key per run; outbox; at-least-once messaging with idempotent handlers; per-chunk local transactions; calculation traces stored with invoices; approval step before fee debits are sent to custodians; metrics and alerting on throughput and failures; shadow mode for new engine versions; what happens when a custodian file arrives late (AUM snapshot versioning).

### 12.4 STAR stories to prepare (mapped to your background)

| Theme | Source | Angle for Brasswick |
|---|---|---|
| Proving correctness of critical software | Canadarm3 flight software, Software Quality Lead | Golden-master and parity testing for fees; "can't be wrong" systems |
| Requirements traceability | TraceQ, defence programs | Tracing fee agreement terms → schedule config → test → invoice |
| Building tools that scale a team | SeamQ, SurfaceQ, GaugeQ, DocQ | Migration analyzers and playbooks as an SME |
| Financial domain | RBC | Regulated environment, auditability, change control |
| Legacy to modern | Your .NET history since the Framework era | A specific migration you drove: what broke, what you'd do differently |
| Fullstack | 17+ years Angular + .NET | AngularJS → Angular route-level strangler |

Write each as: **Situation (2 sentences) → Task (1) → Action (3–4, specific and technical) → Result (with a number).**

### 12.5 Questions to ask them

1. Where is the modernization today? What's migrated, what's the target .NET version, and is anything on .NET 8 facing the November deadline?
2. How do you currently validate that fee calculations haven't changed after a release?
3. Is the deployment model multi-tenant SaaS, single-tenant per client, or a mix? (Big enterprise clients often demand dedicated instances.)
4. What's the hosting target: Azure App Service, Container Apps, AKS?
5. What's the frontend stack, and is it being modernized in the same program?
6. For the SME role, what's the split between hands-on delivery and enabling other teams?
7. What's the biggest thing that's gone wrong in the migration so far?
8. How does the team make and record architecture decisions?

### 12.6 Things to avoid saying

- "We'll just rewrite it." (Signals you've never shipped a rewrite.)
- "Upgrade Assistant/Copilot will handle most of it." (Tools handle project files; not architecture, not parity.)
- "We'll fix the rounding while we're in there." (See WP-02.)
- Anything that sounds like a migration can be "done" without a production parallel run.

---

## 13. Cheat Sheet: Framework → Modern .NET Mappings

| .NET Framework | Modern .NET (10) |
|---|---|
| `System.Web`, `HttpContext.Current` | `IHttpContextAccessor` (sparingly); pass data explicitly |
| `Global.asax` | `Program.cs`, middleware pipeline |
| HTTP modules/handlers | Middleware |
| Web API 2 `ApiController`, `IHttpActionResult` | `ControllerBase`, `IActionResult` / minimal APIs + `TypedResults` |
| `HttpResponseMessage` returns | `IResult` / `ActionResult<T>` |
| MVC 5 filters | ASP.NET Core filters / endpoint filters |
| `Web.config` / `ConfigurationManager` | `appsettings.json`, `IOptions<T>`, env vars, Key Vault |
| Config transforms | Environment-specific `appsettings.{Env}.json`, App Configuration labels |
| Unity / Autofac / Ninject | `Microsoft.Extensions.DependencyInjection` (Autofac still available if needed) |
| log4net / NLog direct | `ILogger<T>` + provider (Serilog, OpenTelemetry) |
| `HttpRuntime.Cache` / `MemoryCache.Default` | `IMemoryCache`, `HybridCache`, `IDistributedCache` |
| Forms auth / Membership | ASP.NET Core Identity or OIDC (Entra ID) |
| `machineKey` | Data Protection API (shared key ring during transition) |
| WCF server | CoreWCF, gRPC, or REST |
| WCF client | `System.ServiceModel.*` client packages (supported) |
| Windows Service | Worker Service (`UseWindowsService()` if staying on Windows) |
| EF6 + EDMX | EF Core (code-first or reverse-engineered, migrations) |
| `TransactionScope` across databases | Outbox pattern, sagas, eventual consistency |
| `BinaryFormatter` | `System.Text.Json`, protobuf, MessagePack |
| `AppDomain`, .NET Remoting | Processes, containers, gRPC |
| `System.Drawing` | SkiaSharp, ImageSharp, QuestPDF (check licence) |
| `packages.config` | `PackageReference` + Central Package Management |
| Old-style csproj | SDK-style csproj |
| `Encoding.Default` (ANSI) | UTF-8 by default; register `CodePagesEncodingProvider` for legacy code pages |
| NLS globalization (Windows) | ICU globalization |
| AngularJS 1.x | Angular (standalone, signals), route-level strangler or ngUpgrade |

---

*Good luck, Quinn. Do WP-01 properly. It's the story that will carry the interview.*
