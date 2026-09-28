# 23 · Cutover, Shadow Runs and Decommissioning

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-10 · **Prerequisites:** 01, 04, 05, 15, 16

**Audio lesson:** [23-cutover-shadow-runs-and-decommission.mp3](23-cutover-shadow-runs-and-decommission.mp3) · [Transcript](script.md)

## Why this video exists

"When is the migration done?" is the question that separates people who have shipped a migration from people who have written one. For a billing system the answer isn't "when the new code works". It's when the new engine has billed real clients, the old one has been switched off, and nobody noticed. This video covers the last mile for FeeBilling: shadow runs as evidence, firm-by-firm cutover behind flags, what "rollback" means once invoices have been approved and fees debited, and a decommission checklist that finds the consumers nobody documented.

## Learning objectives

By the end, the viewer can:

- Design shadow mode: the modern engine runs on identical inputs, writes to a shadow table, and a nightly diff classifies every difference.
- Set objective exit criteria for leaving shadow mode, and name who signs off.
- Cut over one firm at a time with a flag that is evaluated once per run and recorded on it.
- Define rollback precisely for each invoice state (draft, approved, debited), using compensating actions instead of deletes.
- Ship the behaviour fix (`BillingPolicy.Corrected`) as a separate, separately approved change, with client communications that quantify its impact.
- Run a decommission that deletes things in a safe order and finds hidden consumers first.
- Structure a cutover runbook: go/no-go criteria, owners, timings, communications and verification queries.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do you cut over a billing system? | A full quarter of shadow runs for every firm. Exit when there are zero unexplained differences for two consecutive runs. Then cut over firm by firm behind a flag, simplest firm first, with legacy kept runnable for a full cycle after each firm. Rollback is rehearsed, not assumed. |
| What does rollback mean once invoices have been sent? | It depends on state. Drafts can be voided and re-run. Approved invoices are voided and reissued with an audit link. Once fees are debited, money has moved, so rollback means compensating credits or next-period adjustments and reconciliation. Never deletes. Decide this *before* cutover, with finance and compliance. |
| When is a migration done? | When the legacy system is switched off, its fallback route has seen zero traffic for an agreed period, its data is archived under retention rules, and its CI job, infrastructure, accounts and secrets are gone. Until then you are paying for two systems. |
| You found a legacy bug during the migration. When do you fix it? | After cutover, as a separate change behind a per-firm flag with business sign-off and quantified client impact. Migration and behaviour change never ship together. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `src/FeeBilling.Gateway/appsettings.json` | `fallback` route and `legacy-iis` cluster: the last things you delete |
| `legacy/FeeBilling.Web/Controllers/Api/AccountsController.cs` | `// MIGRATED ... Kept for rollback. Do not add features here.` |
| `legacy/FeeBilling.Web/Controllers/Api/BillingController.cs` | `ApproveRun`: raw SQL to `Status = 'Approved'`, with no way back |
| `legacy/FeeBilling.Web/App/billing/run-invoices.controller.js` | `confirm('Approve all draft invoices ... This cannot be undone.')` |
| `database/billing/001-schema.sql` | `Invoices.Status` default `'Draft'`; the `FeeDebits` table |
| `database/billing/002-stored-procedures.sql` | `usp_ReconcileFeeDebits`: billed vs debited per invoice |
| `legacy/FeeBilling.Web/Web.ClientX.config` | `Billing.DayCountBasis` = `Actual/365`: a client whose contract legacy doesn't honour |
| `legacy/FeeBilling.BillingRunner/ProjectInstaller.cs` | `FEEBILLING\svc-billing` domain account "(MSDTC needs network access)" |
| `legacy/FeeBilling.Ingestion.Wcf/ICustodianFeedService.cs` | "Invoked every 15 minutes by the FeedProcessor scheduled task on APP01": a hidden consumer |
| `legacy/FeeBilling.Data/FeeBillingEntities.Partial.cs` | "Added for the month-end export tool": another one |
| `src/FeeBilling.Domain/FeeBilling.Domain.csproj` | "Retarget to net10.0 once no Framework project references it." |
| `.github/workflows/ci.yml` | The `legacy` job on `windows-latest` |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | A migration isn't done when the new code works. It's done when legacy is off and nobody noticed. Everything in this video exists to make "nobody noticed" true. |
| 01:30–05:30 | Shadow mode | The modern worker runs with `BillingPolicy.Legacy` on the *same inputs* as legacy and writes `ShadowInvoices` (a future table, from the training plan) with calculation traces. Legacy stays the system of record. Inputs must be identical: if a custodian file lands between the two runs, they read different AUM, so snapshot the inputs. The nightly diff uses the parity-report categories from video 04. Exit criteria: a full quarter for all firms, and zero unexplained differences for two consecutive runs (run shadow on month-end data to get more samples than quarterly billing gives you). After cutover, reverse it: legacy runs in shadow for one cycle. |
| 05:30–08:30 | Firm-by-firm cutover | A `BillingEngine = Legacy \| Modern` flag per firm (Microsoft.FeatureManagement or App Configuration). Evaluate it **once, when the run is enqueued**, and store the result on the run, so a run never changes engine halfway through. Which firm goes first? The seed shows the tension: Maple Ridge is smallest (13 accounts) but exercises every edge case (S1 to S10); Laurentien is larger but more uniform, and brings `fr-CA` ingestion (S12). Pick using explicit criteria. Keep the engine flag and the policy flag separate. |
| 08:30–12:30 | What rollback means | Walk the state table below. Legacy stays runnable for a full cycle after each firm's cutover: IIS up, the Windows Service installed but disabled, and the schema still readable by the EDMX (expand/contract, video 14). A monthly rollback drill proves it. Rows the modern engine wrote must stay readable by legacy, which is why new statuses such as `Voided` are an *expand* change. |
| 12:30–14:00 | Then, and only then, fix behaviour | `BillingPolicy.Corrected` (decimal throughout, Actual/365, largest-remainder allocation) is a second change with its own flag, sign-off and communications. Quantify it: S1 moves from 5,312.50 to 5,356.16 per quarter, +43.66 on a $2.5M account. ClientX's contract has said Actual/365 all along. That's a conversation for account management, not a line in a release note. |
| 14:00–17:30 | Decommissioning | Order matters: measure, then switch off, then delete. The gateway's `fallback` route must show zero hits for an agreed period before it goes. Find invisible consumers first: the FeedProcessor scheduled task on APP01, the month-end export tool, the `\\fileserver01\custodian-feeds\archive` share, and whoever parses the `{ "Table": [...] }` shape. Then walk the checklist below. Rotate every secret that was ever committed (`machineKey`s, database passwords in `Web.Release.config` and `Web.ClientX.config`). Deleting a file doesn't remove it from git history. |
| 17:30–19:00 | The runbook | Walk the runbook skeleton below. Every step has an owner (a role, not a person), a time, and a verification with an expected result. Rollback has a trigger and a decision owner written down in advance. |
| 19:00–20:00 | Recap | Evidence before cutover, one firm at a time, rollback defined by invoice state, behaviour fixes separately, and delete legacy only once you've measured that nothing uses it. |

### Rollback by invoice state

| State when the problem is found | What rollback means | Mechanism |
|---|---|---|
| Run complete, invoices `Draft` | Void the modern run and re-run on legacy | Set a status such as `Voided` (an expand change legacy must tolerate). Keep both runs for audit. |
| `Approved`, no fee debit instructed yet | Void and reissue | Void with a reason and a link to the replacement invoice. Approval is redone. The reviewer is told. |
| Fee debit instructed or collected (`FeeDebits` rows exist) | Money has moved: correct it, don't erase it | A compensating credit or next-period adjustment, reconciled with `usp_ReconcileFeeDebits`, plus client communication. Finance and compliance own the decision. |

The legacy approve button already says "This cannot be undone." That's the honest version of this table. The migration's job is to make each row a documented procedure, not an incident.

### Choosing the engine per firm, once per run (sketch)

```csharp
public enum BillingEngine { Legacy, Modern }

public sealed class BillingEngineSelector(IFeatureManager features)
{
    public async Task<BillingEngine> SelectAsync(string firmCode)
    {
        var context = new TargetingContext { UserId = firmCode, Groups = [firmCode] };
        return await features.IsEnabledAsync("ModernBillingEngine", context)
            ? BillingEngine.Modern
            : BillingEngine.Legacy;
    }
}

// At enqueue time only. The chosen engine is stored on the run row and never re-evaluated.
run.Engine = await selector.SelectAsync(firm.FirmCode);
```

Verify the FeatureManagement targeting API for the package version you use. The design point is version-independent: **the flag decides at enqueue time, and the run records the decision.**

### Verification queries for the runbook (sketch; adjust to billing rules)

```sql
-- Every active account in the firm has an invoice in the run
SELECT a.Id, a.AccountNumber
FROM dbo.Accounts a
WHERE a.FirmId = @FirmId AND a.IsActive = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.Invoices i WHERE i.RunId = @RunId AND i.AccountId = a.Id);

-- No account billed twice in one run
SELECT AccountId, COUNT(*) AS InvoiceCount
FROM dbo.Invoices
WHERE RunId = @RunId
GROUP BY AccountId
HAVING COUNT(*) > 1;

-- After custodian debits land: billed vs debited
EXEC dbo.usp_ReconcileFeeDebits @RunId = @RunId;
```

### Decommission checklist

| Area | Item | Evidence before deleting |
|---|---|---|
| Routing | Remove the `fallback` route and `legacy-iis` cluster from the gateway | Zero fallback hits for the agreed period (gateway metrics) |
| Legacy web | Delete the `MIGRATED` controllers, the SystemWebAdapters remote-app server in `Global.asax.cs`, and `RemoteAppApiKey`; remove `AddRemoteAppClient` from Accounts.Api | New auth (video 20) is live for all firms |
| Ingestion | Retire the CoreWCF shim; migrate or archive `BinaryFormatter` payloads in `StagedBatches` | All three firms' feed agents upgraded; no SOAP calls for the period |
| Billing runner | Uninstall the Windows Service; remove MSDTC configuration; disable the `svc-billing` domain account | Legacy not needed for rollback any more (a full cycle has passed for every firm) |
| Data | Archive the EDMX; EF Core migrations own the schema; the Reporting DB is fed by events, not `TransactionScope` | Contract (drop) migrations reviewed; nothing reads the dropped columns |
| Shared code | Retarget `FeeBilling.Domain` from `netstandard2.0` to `net10.0`; drop the C# 7.3-friendly constraints | No Framework project references it |
| Hidden consumers | FeedProcessor scheduled task on APP01, month-end export tool, custodian feed archive share, SMTP settings | Each has an owner who confirmed the replacement |
| CI/CD | Remove the `legacy` job from `.github/workflows/ci.yml` and `legacy/` from the repo | Tagged release of the final legacy state for reference |
| Secrets | Rotate everything ever committed: `machineKey`s, connection string passwords, the remote-app API key | Rotation confirmed; old values rejected |
| Retention | Archive logs (`C:\Logs\FeeBilling`), generated invoices and database backups under the regulated retention policy | Retention owner sign-off |

### Runbook skeleton (per firm)

1. **T−2 weeks:** Shadow exit criteria met, with a link to the parity reports. Sign-offs from product, finance/compliance and client success. Client communication sent if anything is visible.
2. **T−1 day:** Freeze fee schedule edits for the firm (a mid-quarter edit changes open invoices). Snapshot the billing inputs.
3. **T0:** Flip `ModernBillingEngine` for the firm. Enqueue the run. Run the verification queries, with expected results written in the runbook in advance.
4. **T0 + review:** Reverse shadow. Legacy computes the same run in shadow; the diff must be zero, or every difference must be explained.
5. **Rollback trigger:** Written conditions (for example, any unexplained difference over $0.01, or a missing account) and the role that makes the call.
6. **T + 1 cycle:** Legacy no longer needed for this firm. Record that in the decommission tracker.

## Demo

```bash
# What still depends on legacy? Start the inventory from the code's own comments.
git grep -n "MIGRATED\|Kept for rollback" -- legacy
git grep -n "scheduled task\|export tool\|fileserver01" -- legacy
git grep -n "fallback\|legacy-iis" -- src/FeeBilling.Gateway

# The reconciliation you'll run after cutover exists today
git grep -n "usp_ReconcileFeeDebits" -- database legacy
```

## Traps to call out

- **Leaving shadow mode on a feeling.** "It looked right" isn't an exit criterion. Two consecutive runs with zero *unexplained* differences is.
- **Different inputs, false differences.** Shadow and legacy must read the same AUM snapshot. Otherwise a late custodian file shows up as an engine difference.
- **Re-evaluating the flag mid-run.** Flipping a firm's flag while its run is processing must not mix engines within one run. Decide at enqueue time and store the decision.
- **Rollback by `DELETE`.** Invoices and debits are financial records. Rollback is void, reissue, credit and reconcile, with an audit trail.
- **Fixing the bug during cutover.** If invoices change on cutover day, you can't tell whether the engine or the policy did it. Two flags, two changes, two sign-offs.
- **Deleting the fallback route first.** Measure it first. The last consumer of legacy is usually one nobody wrote down.
- **"Decommissioned" but still paying.** The Windows Service account, IIS servers, MSDTC firewall rules, certificates and the CI runner all cost money and carry risk until they're gone.
- **Assuming deleting a file removes a secret.** Git history keeps it. Rotate.

## Key terms

Shadow mode · reverse shadow · exit criteria · go/no-go · feature flag targeting · system of record · compensating action · void and reissue · reconciliation · expand/contract · decommission · data retention · runbook

## After the video

1. Write the cutover runbook for one firm using the skeleton above, including the verification queries and expected results.
2. Fill in the rollback-by-state table with finance's actual procedures, and list what you'd need to ask them.
3. Start a decommission tracker: every legacy component, its hidden consumers, and the evidence required before it's deleted.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 8 (principles 3 to 5), Section 9 WP-04 (shadow mode) and WP-10, Section 12.1 (walkthrough step 6), Section 12.6 (things to avoid saying)
- Video 04 (parity report categories), video 05 (`BillingPolicy.Legacy` vs `Corrected`), video 14 (expand/contract), video 16 (worker and events)
- Microsoft Learn: *Feature management in .NET* (Microsoft.FeatureManagement), *Azure App Configuration feature flags*
- Martin Fowler, *Feature Toggles* and *ParallelChange* (expand/contract)
