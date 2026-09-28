# 23 · Cutover, Shadow Runs and Decommissioning

Welcome. "When is the migration done?" is the question that separates people who have shipped a migration from people who have only written one. For a billing system, the answer isn't "when the new code works". It's when the new engine has billed real clients, the old one has been switched off, and nobody noticed. By the end of this lesson, you should be able to design shadow runs as evidence, cut over one firm at a time, define exactly what rollback means once invoices have been approved and fees debited, and decommission the legacy system without breaking a consumer nobody wrote down.

## The questions this lesson answers

Here are the questions this lesson prepares you for. How do you cut over a billing system? What does rollback mean once invoices have been sent? When is a migration done? And a follow-up that tests discipline: you found a legacy bug during the migration, so when do you fix it?

Keep the goal in mind: legacy is off, and nobody noticed. Everything in this lesson exists to make "nobody noticed" true. That takes evidence before cutover, small steps during it, a rollback that's defined before you need it, and a decommission that measures before it deletes.

## Shadow mode: evidence before cutover

Golden-master tests, from lesson four, prove the new engine matches legacy on the seed scenarios. Shadow mode proves it on real production data, at full scale, over time.

Here's how it works. The modern billing worker runs with `BillingPolicy.Legacy`, the preset that reproduces legacy behaviour exactly, including its rounding quirks. It runs on the same inputs as the legacy billing run, and writes its results to a separate table. The training plan calls it `ShadowInvoices`; it doesn't exist yet in the repository. Each shadow invoice stores a calculation trace: the AUM used, the tiers applied, the day-count basis and the rounding. Legacy stays the system of record. Clients only ever see legacy invoices.

Then a nightly job diffs shadow against legacy, account by account, and classifies every difference using the parity-report categories from lesson four: double precision, rounding mode, day count, allocation penny, and so on. Every difference is either explained, with a link to an ADR or a ticket, or it's a bug.

What does "explained" mean in practice? The known-differences allowlist from lesson four. Each entry names a category, the accounts or scenarios it covers, the maximum amount, and the ADR or ticket that justifies it. A difference that matches an entry is explained. Anything else fails the nightly check and pages someone. And the report isn't just a pass or fail: it shows, per firm, how many accounts matched exactly, how many differed and in which categories, and the total dollar impact. That report is the artifact you'll show at the go or no-go meeting, and it's also a great thing to describe in an interview.

There's a subtle trap in "same inputs". If a custodian position file lands between the legacy run and the shadow run, they read different AUM, and you get a difference that has nothing to do with the engine. So snapshot the billing inputs, and run both engines against the same snapshot.

When do you leave shadow mode? Not on a feeling. The exit criteria from the training plan are a full quarter of shadow runs for all firms, and zero unexplained differences for two consecutive runs. Quarterly billing only gives you four runs a year, so run shadow on month-end data as well, to get more samples. And name who signs off: product, finance or compliance, and client success.

After a firm cuts over, reverse it. The modern engine becomes the system of record, and legacy runs in shadow for one cycle. The diff must still be zero, or every difference explained. That's your safety net for the first live run.

## Firm-by-firm cutover

Never cut over all forty firms at once. The unit of cutover is the firm, controlled by a flag: billing engine, legacy or modern, per firm. You can implement it with Microsoft.FeatureManagement or with Azure App Configuration feature flags, targeting individual firms.

The most important design point is when the flag is evaluated. Evaluate it once, when the run is enqueued, and store the chosen engine on the run record. Never re-evaluate it mid-run. Otherwise, someone flips a firm's flag while its run is processing, and one run is half legacy and half modern, and nobody can explain the invoices. The flag decides at enqueue time, and the run records the decision. That principle doesn't depend on which feature-flag library or version you use. Treat the flag itself as a controlled change, too. Flipping a firm to the modern engine should need the same approval as a deployment, and the flag store should keep an audit history of who changed what and when, because "which engine billed this invoice, and who decided that?" is exactly what an auditor will ask.

Which firm goes first? The seed data shows the real tension. Maple Ridge is the smallest, with thirteen accounts, but it exercises every edge case, scenarios one through ten: tiers, boundaries, minimum fees, blended and flat schedules, households, zero-AUM households, a mid-quarter opening, and a large withdrawal. Groupe Financier Laurentien is larger, five hundred accounts, but more uniform, and it brings French-Canadian custodian files. There's no single right answer. The right answer is to pick using explicit criteria, such as size, complexity, client relationship and data quality, and write them down.

And keep two flags separate: the engine flag, which says whether the modern engine bills this firm, and the policy flag, which says whether it uses legacy behaviour or corrected behaviour. More on that in a moment.

## What rollback means

Rollback in a billing system isn't "redeploy the old version". It depends on how far the invoices have travelled. Look at the invoice states in `database/billing/001-schema.sql`: invoices default to a status of Draft, and there's a separate `FeeDebits` table recording what each custodian actually debited. Then look at how approval works in `legacy/FeeBilling.Web/Controllers/Api/BillingController.cs`: the approve action runs a raw SQL update that sets drafts to Approved. There's no way back. The legacy UI is honest about it: the approve button in `run-invoices.controller.js` asks for confirmation with the words "This cannot be undone."

So define rollback per state, before cutover, with finance and compliance.

If the run is complete and the invoices are still drafts, rollback means voiding the modern run and re-running on legacy. You mark the run with a status such as Voided, and you keep both runs for audit. Voided is a new status legacy has never seen, so it's an expand change from lesson fourteen: legacy must tolerate it before you ever use it.

If invoices are approved but no fee debit has been instructed yet, rollback means void and reissue. You void each invoice with a reason and a link to its replacement, approval is redone, and the reviewer is told.

If a fee debit has been instructed or collected, money has moved. You don't erase it; you correct it. That means a compensating credit or a next-period adjustment, reconciled with `usp_ReconcileFeeDebits`, the stored procedure that already compares billed against debited for every invoice in a run, plus communication to the client. Finance and compliance own that decision.

Notice what's missing from all three: DELETE. Invoices and debits are financial records. Rollback is void, reissue, credit and reconcile, always with an audit trail.

Rollback also has to stay possible. Legacy stays runnable for a full billing cycle after each firm's cutover: IIS up, the Windows Service installed but disabled, and the database schema still readable by the legacy EDMX model, which is why every schema change follows expand and contract. And don't assume rollback works. Rehearse it with a monthly drill.

## Then, and only then, fix behaviour

During the migration, you'll have found legacy bugs. Lesson five covered the big ones: money calculated in double, per-tier rounding, a hard-coded divide by four, and household allocations that can miss by a penny. The fix is a preset called `BillingPolicy.Corrected`: decimal throughout, actual/365 day count, rounding at the end, and largest-remainder allocation.

It ships after cutover, as a separate change, with its own flag, its own sign-off and its own client communication. Quantify it. Scenario S1, a $2.5 million account on the standard tiered schedule, goes from $5,312.50 a quarter under legacy to $5,356.16 under actual/365. That's $43.66 more per quarter for that one client.

And there's a twist. Open `legacy/FeeBilling.Web/Web.ClientX.config`. It sets `Billing.DayCountBasis` to Actual/365, because that single-tenant client's fee agreement says actual/365. But nothing in the code reads that setting. The calculators always divide by four. So ClientX has been billed differently from their contract for years. That's not a line in a release note. It's a conversation for account management, with numbers.

What do client firms need to know? Give each firm an impact report, produced by running the corrected policy in shadow against their real data: the total change in fees per quarter, the largest change for any single account, how many accounts go up or down, and how the change interacts with minimum fees, because a higher basis can lift a small account above its minimum. Then agree an effective date, and whether the firm opts in or the change is contractual. Some firms will want the corrected basis immediately. Others will want to warn their own clients first. The per-firm policy flag is what lets you honour both.

If invoices change on cutover day, you can't tell whether the engine or the policy did it. Two flags, two changes, two sign-offs.

## Decommissioning in a safe order

Decommissioning has an order: measure, then switch off, then delete.

The last things you delete are the gateway's fallback route and the legacy cluster, in `src/FeeBilling.Gateway/appsettings.json`. Before removing them, the fallback route must show zero traffic for an agreed period, measured with gateway metrics. The last consumer of a legacy system is usually one nobody documented.

So find the invisible consumers first, and the code leaves clues. `legacy/FeeBilling.Ingestion.Wcf/ICustodianFeedService.cs` says the batch-processing operation is invoked every fifteen minutes by the FeedProcessor scheduled task on a server called APP01. `legacy/FeeBilling.Data/FeeBillingEntities.Partial.cs` has a constructor added for the month-end export tool. `Web.config` points at a network file share on a server called fileserver01, holding the custodian feed archive. And somewhere, someone may parse the serialized data-set shape of the invoice export. Each of these needs an owner who confirms its replacement.

Then walk the checklist. Routing: remove the fallback route. Legacy web: delete the controllers marked as migrated and kept for rollback, like `AccountsController.cs`, remove the SystemWebAdapters remote-app server from `Global.asax.cs`, and its API key. Ingestion: retire the CoreWCF shim once all three firms' feed agents are upgraded, and migrate or archive the `BinaryFormatter` payloads in the staging table. Billing runner: uninstall the Windows Service, remove the MSDTC configuration, and disable the domain account it ran as. `ProjectInstaller.cs` says it runs as a domain account called svc-billing, because MSDTC needs network access. Data: archive the EDMX, and let EF Core migrations own the schema. Shared code: retarget `FeeBilling.Domain` from `netstandard2.0` to `net10.0`. The project file's own comment says to do that once no Framework project references it. CI: remove the legacy job from `.github/workflows/ci.yml`, and tag the final legacy state for reference.

Two items people forget. Secrets: rotate everything that was ever committed, including the machine keys and the database passwords in `Web.Release.config` and `Web.ClientX.config`. Deleting a file doesn't remove it from git history. And retention: archive logs, generated invoices and database backups under the regulated retention policy, with the retention owner's sign-off.

When is a migration done? When legacy is switched off, its fallback route has seen zero traffic for the agreed period, its data is archived under retention rules, and its CI job, servers, accounts and secrets are gone. Until then, you're paying for two systems.

## The cutover runbook

Everything above becomes a runbook, one per firm. Every step has an owner, named as a role rather than a person, a time, and a verification with an expected result written in advance.

Two weeks before: shadow exit criteria met, with links to the parity reports, sign-offs from product, finance and client success, and client communication sent if anything will be visible.

The day before: freeze fee schedule edits for the firm, because a mid-quarter schedule edit changes open invoices, and snapshot the billing inputs.

On the day: flip the flag for the firm, enqueue the run, and run the verification queries. For example, every active account in the firm has an invoice in the run, and no account is billed twice in one run.

Then reverse shadow: legacy computes the same run, and the diff must be zero or fully explained.

The rollback trigger is written down in advance, for example any unexplained difference over one cent, or a missing account, along with the role that makes the call.

And one full cycle later, legacy is no longer needed for this firm, and that's recorded in the decommission tracker.

## Traps

Leaving shadow mode on a feeling. Two consecutive runs with zero unexplained differences is an exit criterion; "it looked right" isn't.

Different inputs producing false differences. Snapshot the AUM.

Re-evaluating the flag mid-run. Decide at enqueue time and store the decision.

Rollback by DELETE. Void, reissue, credit and reconcile.

Fixing the bug during cutover.

Deleting the fallback route before measuring it.

"Decommissioned", but still paying for the service account, the servers, the firewall rules and the CI runner.

And assuming that deleting a file removes a secret.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do you cut over a billing system?

[pause 5s]

With evidence first. A full quarter of shadow runs for every firm, with the modern engine reproducing legacy behaviour on the same input snapshot, and a nightly diff that classifies every difference. I'd exit shadow only after two consecutive runs with zero unexplained differences, with named sign-offs. Then cut over firm by firm behind a flag that's evaluated once at enqueue time and stored on the run, starting with a firm chosen by explicit criteria. Legacy stays runnable for a full cycle after each firm, runs in reverse shadow, and rollback is rehearsed, not assumed.

**Interviewer:** What does rollback mean once invoices have been sent?

[pause 5s]

It depends on the invoice state. Draft invoices can be voided and the run redone on legacy. Approved invoices are voided and reissued, with an audit link between them. Once a fee has been debited, money has moved, so rollback means a compensating credit or next-period adjustment, reconciled against the custodian's debits, and communicated to the client. Never deletes. And all of that is agreed with finance and compliance before cutover, not during an incident.

**Interviewer:** When is a migration done?

[pause 5s]

When the legacy system is switched off and nobody noticed. Concretely: the gateway's fallback route has seen zero traffic for an agreed period, hidden consumers like scheduled tasks and export tools have owners and replacements, legacy data is archived under retention rules, and the legacy CI job, servers, service accounts and secrets are gone, with every committed secret rotated. Until then, you're running and paying for two systems.

**Interviewer:** You found a legacy bug during the migration. When do you fix it?

[pause 5s]

After cutover, as a separate change behind its own per-firm flag, with business sign-off and the client impact quantified. For example, moving from divide-by-four to actual/365 changes a $2.5 million account's quarterly fee by $43.66. Migration changes and behaviour changes never ship together, because if an invoice changes on cutover day, you need to know which one caused it.

## Recap

Five things to remember from this lesson.

One: shadow runs are the evidence. Same inputs, a nightly classified diff, and objective exit criteria with named sign-offs.

Two: cut over one firm at a time, with the engine chosen once at enqueue time and recorded on the run.

Three: rollback is defined per invoice state, and it's void, reissue, credit and reconcile. Never delete financial records.

Four: behaviour fixes ship after cutover, separately, quantified and communicated.

Five: decommission in order: measure, switch off, then delete. Find the hidden consumers first, and rotate every secret that was ever committed.

In the final lesson, we'll turn everything in this series into CI guardrails and team enablement, so the rest of the team can finish the migration safely.
