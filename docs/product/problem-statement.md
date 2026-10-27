# Problem statement

## Context
Chubb APAC handles motor and property claims in six markets (SG, HK, MY, TH, ID, AU). Claims move through several people (claimant, claims officer, manager), but nobody can easily see what happened to a claim, when, by whom and why. Once money is involved the gap gets worse: who proposed the payout, who approved it, whether the bank actually paid, and whether the books agree. This take-home builds a thin claims platform, backend first, within a 2-3h target and a hard 5h cap.

## Who is affected and the pain
- **Claimant:** reports a loss, then waits. Cannot see progress, does not know when information is needed, why a claim was rejected, or when and how much will be paid, so they phone in.
- **Claims officer:** picks up, assesses and proposes decisions. Loses time reconstructing history, risks two officers taking the same claim, has no clear view of own workload, and cannot tell whether a proposal is waiting on a manager.
- **Claims manager:** needs outstanding claims and liability exposure now, not from a monthly report. Must act as the second pair of eyes on every decision and settlement, handle failed payments, keep the pool fund topped up, and answer a dispute ("who changed the reserve, who approved it, when?") without evidence gaps.

## Focus: the claim progress trail (the centrepiece)
The assessment wants to see the **trail of claim progress**: a transparent, immutable, ordered history of every step of a claim (who, when, what, why), with **Public** entries for the claimant and **Internal** entries for staff only. Everything else (queues, decisions, payments, ledger, dashboards) either writes to the trail or is derived from it. Money steps are trailed too: proposal, confirmation (maker and checker named, Internal), payee details, payment attempts, completion, and every refused attempt (Internal). Workload, handling time and time-in-status come from the same trail, so numbers and history never disagree.

## Money movement, control and auditability
Handling a claim now ends in real money movement, so the platform adds four controls, each recorded in or tied to the trail:
1. **Maker-checker (four-eyes) on decision and settlement.** The assigned officer proposes; only a Manager who is not the maker may confirm or send back. Nothing financial changes until the second signature (BR-31 to BR-40).
2. **Payment.** A separate Payment service creates one payment per confirmed settlement, pays through a simulated bank gateway with idempotency, 3 retries and a manager exceptions queue. The claim becomes Settled only when PaymentCompleted arrives. The claimant supplies payee bank details after approval; they are masked everywhere (BR-41 to BR-53).
3. **General ledger.** A double-entry, append-only ledger records reserve, payable and cash movements from events, with a trial balance and reconciliation that ties to exposure. Manager-only (BR-54 to BR-63).
4. **Pool fund.** One fund per market and currency pays claims; a funds check holds a payment that cannot be covered; managers top up and see the coverage ratio. Claimants have no wallet; they see only payable, paid-to-date and remaining coverage limit (BR-66 to BR-73).

## Goals
1. Claimants can follow their claim end to end, understand every decision and know what is payable and paid.
2. Staff can work claims safely (one owner, valid transitions only, reasons captured, no one approves their own work).
3. Managers see outstanding claims and liability exposure (definition in `docs/domain/glossary.md`) and control money-affecting steps.
4. Every change, including refused attempts, is auditable and cannot be rewritten; money in the ledger ties back to the trail.

## Scope honesty: tiers and the 5h cap
The scope grew far beyond a 5h build: it now spans maker-checker, a Payment service, a General Ledger service and a Pool Fund module on top of the original claims, reporting and notification services. We therefore build in tiers (detail and story ids in `docs/product/user-stories.md`):
- **Tier 1 (approved for the 5h cap):** thinnest end-to-end slice showing the trail and maker-checker: submit, pick up, assess, propose, manager queue, confirm or send back, claimant and staff timelines, outstanding and exposure dashboards. The demo ends at a confirmed Approved or Rejected claim.
- **Tier 2A (only if time remains):** information request and answer, withdraw, reassign, refused attempts trailed, workload.
- **Tier 2B (payment + ledger + fund): NOT approved for the 5h cap.** It is specified (rules and stories) but a realistic 5h build does not reach it; building it needs the human either to extend the timebox or to accept it as "designed, not built".
- **Tier 3:** nice-to-have.

## What we build
Tier 1: Claims service with state machine (including the two pending-approval states), trail (`GET /claims/{id}/timeline`), pick-up, assessed-amount, propose / confirm / send-back and the manager pending queue; Reporting service (outstanding, exposure) fed by Kafka events; mock-login Identity and a Gateway; one Angular app with claimant, staff and manager areas sharing one timeline component. Tier 2B adds Payment, Ledger and Pool Fund modules (service boundaries are the architect's decision). Rules: `docs/domain/business-rules.md`.

## Success measures (demo-checkable)
Tier 1:
- Demo path under 5 minutes: claimant submits, officer picks up and assesses, officer proposes approval, the same officer cannot confirm (403), a manager confirms, claimant sees "approved", manager dashboard updates.
- Every accepted transition adds exactly one trail entry (BR-04); a send-back adds one Internal entry and the claimant sees no trace of it (BR-33).
- Claimant timeline shows only Public entries; the same claim as officer or manager shows more; no count or gap reveals Internal entries (BR-09).
- No trail entry can be updated or deleted; a correction is a new entry (BR-06).
- Two simultaneous confirms: exactly one succeeds, the other gets 409 (BR-38). Two simultaneous pick-ups likewise (BR-26).
- Exposure equals the sum of reserve of non-final seeded claims in USD, and proposing, sending back and withdrawing leave it unchanged (BR-22, BR-24, BR-35).
- `docker compose up` starts the system with seeded users and about 30 claims.
Tier 2B (only if built):
- A claim is Settled only after PaymentCompleted; a failing gateway gives exactly 4 attempts then a manager queue entry (BR-47, BR-48).
- Ledger trial balance nets to zero per currency and fund balance equals ledger account 1000 (BR-60, BR-66).
- A payment larger than available funds is Held, posts nothing, and is released after a top-up (BR-67).

## Constraints
2-3h target, 5h cap; backend before frontend; .NET, Postgres, Kafka; REST for commands and queries, Kafka for domain events; mock auth only; fictitious seeded data; simulated bank gateway only; AI usage recorded in the journal.

## Out of scope
Real SSO, policy-system integration, real bank integration (gateway is simulated), file storage (metadata at most), fraud scoring, email/SMS, reopen/withdraw of final claims, excess engine (excess is a seeded attribute), SLA escalation, per-market regulation, HA, i18n, IBNR, manual journals, ledger period close, FX revaluation, tax, reinsurance, premium collection, fund investment, notifications to makers and checkers, authority limits, delegation (see "Documented, not built" in business rules).
Cut first when time is short (in this order): Tier 3, then Tier 2B entirely, then Tier 2A (workload last), then within Tier 1 the exposure and outstanding dashboards' polish, charts and frontend styling.

## Assumptions
See the confirmation list at the end of `docs/product/user-stories.md`. Insurance terms and rules come from the business analyst, not from this document.
