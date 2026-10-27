# User stories

Priority: MUST / SHOULD / COULD. Tier: T1 / T2A / T2B / T3 (see "Tiers and the 5h cap" below). Rules (BR-xx) are in `docs/domain/business-rules.md`; terms in `docs/domain/glossary.md`. Services: Claims (CS), Reporting (RP), Notification (NT), Identity (ID), Gateway (GW), Frontend (FE), Payment (PY), Ledger (LG), Pool Fund (PF). Module placement of PY/LG/PF is the architect's call; the codes only say where a story touches.
The claim progress trail is the centrepiece; trail stories are marked **[TRAIL]**. Maker-checker stories are marked **[MC]**.

## Tiers and the 5h cap (read first)
Scope grew well past 5 hours. Honest position:
- **Tier 1 (13 stories + 3 enablers), priority MUST.** Thinnest end-to-end slice that still shows the trail and maker-checker. Demo ends at confirmed Approved or Rejected. This is the only tier approved for the 5h cap, and it is already tight (backend first, plain-table frontend).
- **Tier 2A (8 stories), SHOULD.** Claim-handling completeness. Reached only if Tier 1 finishes early; realistic 5h stretch is US-07 and US-04 (info request and answer) and US-22 (refused attempts trailed), because they enrich the trail.
- **Tier 2B (14 stories), COULD: payment + ledger + fund.** Specified, NOT approved for 5h. A realistic 5h build does not reach it; delivering it needs a longer timebox or acceptance as "designed, not built" (rules, stories and the worked example exist; the demo says so).
- **Tier 3 (5 stories), COULD.** Nice-to-have.
Realistic outcome in 5h: Tier 1 complete, plus a few Tier 2A items. Settlement and payment are not demoed.
**Cut order (first cut first):** Tier 3 > Tier 2B (whole tier; within it cut in reverse number order: US-45, 44, 43, 42, 41, 40...) > Tier 2A (US-10, 12, 31, 02, 05, then 22, 04, 07) > inside Tier 1: US-14/13 chart-free minimal tables, US-30 folded into the claim list filter "PendingDecisionApproval", frontend polish. Never cut: US-01, 03, 06, 09, 18, 20, 26 to 29.
Retired or merged ids: US-08 (replaced by US-26 to US-29, US-35 to US-37), US-15 (merged into US-09), US-19 (merged into US-18), US-21 (merged into US-20).

---

# Tier 1 (MUST): the thin slice with trail and maker-checker

**US-01 Report an incident** (MUST, T1)
As a claimant, I want to submit a motor or property claim, so that the insurer starts handling it.
- Given a valid policy number, market, type, incident date, description and amount, When I submit, Then I get a claim number (BR-25), status Submitted, claim currency of my policy's market (BR-23), and exactly one entry: Public, actor Claimant (from the token), text "Claim received: {claimNumber}" (BR-75; it is the submission entry, not a second one).
- Given a missing or invalid field, When I submit, Then I get a validation error and no claim exists.
- Rules: BR-04, BR-08, BR-22 (reserve = estimate), BR-23, BR-25, BR-75. Touches: CS `POST /claims`; FE report form.

**US-03 [TRAIL] See the public timeline of my claim** (MUST, T1)
As a claimant, I want a timeline of what happened to my claim, so that I know where it stands and why.
- Given my claim was submitted and picked up, When I open the detail, Then I see "Claim received: {claimNumber}", "Claim picked up for review" in order with date.
- Given a decision proposal is pending, Then I see only "Decision under review" (no outcome, amount, names). After a send-back I still see only that, never a send-back (BR-33).
- Given Internal entries exist, Then none appear and no count, gap or id reveals them (BR-09).
- Given a confirmed rejection, Then I see code and text; given confirmed approval, "approved for {currency amount}".
- Rules: BR-08, BR-09, BR-12, BR-33. Touches: CS `GET /claims/{id}/timeline`; FE shared timeline component.

**US-06 Pick up a claim from the queue** (MUST, T1)
As an officer, I want to filter incoming claims and pick one up, so that I own it.
- Given Submitted claims, When I filter by market and type, Then only matching claims show.
- Given a Submitted claim, When I pick it up, Then UnderReview, assigned to me, one Public entry.
- Given two officers pick up at once, Then exactly one succeeds, the other gets 409.
- Rules: BR-01, BR-02, BR-26. Touches: CS `GET /claims`, `POST /claims/{id}/assign`; FE staff queue.

**US-26 Set the assessed amount** (MUST, T1)
As the assigned officer, I want to set the assessed (gross) amount, so that the net payable and reserve reflect my evaluation.
- Given UnderReview with policy excess 500, When I set assessed 8,000, Then net payable 7,500 and reserve 7,500, with one Internal entry showing assessed, excess, payable and old/new reserve.
- Given assessed 400 and excess 500, Then payable is 0 (an approve proposal will be refused, BR-19). Given a claim not assigned to me or in a Pending* status, Then 403 / 409.
- Rules: BR-04, BR-19, BR-21, BR-22, BR-36. Touches: CS command (endpoint is the architect's); FE claim detail. (Answers BA request 1.)

**US-27 [MC][TRAIL] Propose a decision** (MUST, T1)
As the assigned officer (maker), I want to propose approve or reject with a rationale, so that a manager can check it before money moves.
- Given UnderReview with an assessed amount, When I propose approve with rationale (min 10 chars), Then PendingDecisionApproval, one Internal entry (outcome, amount, rationale, makerId, proposalId) and one Public "Decision under review".
- Given reject without a valid code (BR-17 list) or text under 10 chars, Then 422. Given approve with payable 0, or without an assessed amount, Then 422. Given payable above the estimate without justification, Then 422.
- Given the claim is already Pending*, Then 409; reserve, paid and exposure are unchanged after proposing (BR-35).
- Rules: BR-01, BR-10, BR-17 to BR-19, BR-21, BR-33, BR-35, BR-36. Touches: CS `POST /claims/{id}/decision-proposals`; FE decision form.

**US-28 [MC][TRAIL] Confirm a decision (checker)** (MUST, T1)
As a manager (checker), I want to confirm a pending decision exactly as proposed, so that the second signature is on record.
- Given PendingDecisionApproval and I am a Manager who is not the maker, When I confirm, Then Approved (reserve = payable) or Rejected (reserve 0), one Internal entry naming makerId and checkerId, one Public final entry (amount or code and text).
- Given I am an Officer-only user, Then 403. Given I am the maker (even if I also hold Manager), Then 403 (BR-32).
- Given two managers confirm at once, Then one succeeds, one gets 409; the decision applies once (BR-38).
- Given a confirm payload, Then it has no editable amount, code or outcome (BR-34).
- Rules: BR-01, BR-02, BR-22, BR-31, BR-32, BR-34, BR-38, BR-39. Touches: CS `POST /claims/{id}/decision-proposals/{pid}/confirm`; FE manager review. (A refused attempt writes its Internal entry in US-22, T2A; Tier 1 only guarantees the 403/409 and no state change.)

**US-29 [MC][TRAIL] Send a decision back** (MUST, T1)
As a manager (checker), I want to send a proposal back with a reason, so that the officer can rework it.
- Given PendingDecisionApproval and I am not the maker, When I send back with a reason (min 10 chars), Then UnderReview, assignee unchanged, one Internal entry (reason, checkerId, proposalId); reserve unchanged.
- Given no reason, Then 422. Given I am the maker or an Officer-only user, Then 403.
- Given the claimant views the timeline, Then they still see only "Decision under review".
- Rules: BR-01, BR-10, BR-33, BR-34, BR-35, BR-39. Touches: CS `.../send-back`; FE manager review.

**US-30 [MC] Manager pending-approval queue** (MUST, T1)
As a manager, I want a list of claims awaiting my check, oldest first, so that proposals do not sit unseen.
- Given claims in PendingDecisionApproval, When I open the queue, Then I see them oldest proposal first with claim number, market, type, amount, maker and age.
- Given I am an Officer, Then I see no checker queue (403); my own pending claims show as "waiting on checker" in my workload.
- Rules: BR-28, BR-02. Touches: RP `GET /dashboard/pending-approvals` (or CS filter); FE manager area. (Settlement approvals join this queue in US-35/36, T2B.)

**US-09 [TRAIL] See the full trail of a claim (officer and manager)** (MUST, T1)
As an officer or manager, I want Public and Internal entries together, so that I have the whole story before acting or answering a dispute.
- Given a claim with both kinds of entries, When I open it, Then entries are ordered by time, each marked Public or Internal, with actor, role, reason, and for proposals maker and checker.
- Given a claim not assigned to me (officer) or any claim (manager), Then I can read the trail but a decision attempt returns 403.
- Given I am a manager, Then I can open any claim, including those not pending (replaces US-15).
- Rules: BR-02, BR-05, BR-09, BR-12, BR-39, BR-74 (Corrections show as Internal). Touches: CS timeline; FE shared timeline component, manager claim detail.

**US-13 Outstanding claims** (MUST, T1)
As a manager, I want counts by status, market and type plus the oldest Submitted claim, so that I see the backlog.
- Given seeded claims, When I open the dashboard, Then outstanding = every non-Rejected, non-Settled claim, counted once under its own status; Pending* are separate rows (never folded into UnderReview or Approved); totals by status, market and type are equal (BR-77). An "awaiting checker" subtotal may be shown, labelled.
- Given Submitted claims with no assignee, Then "oldest unassigned" is the one with the earliest submittedAt (calendar time, shown in hours and days), ties broken by claim number ascending; reassignment, auto-withdraw or release never puts a claim back in it (BR-76).
- Given a claim is submitted, When I refresh, Then counts include it.
- Rules: BR-28, BR-76, BR-77. Touches: RP `GET /dashboard/outstanding`; FE manager dashboard.

**US-14 Liability exposure** (MUST, T1)
As a manager, I want exposure by market and type in USD, so that I know what we may have to pay.
- Given open claims with reserves, Then total = sum(reserve x static rate) over non-final claims; Pending* claims count at their current reserve.
- Given a claim is Rejected, Then its reserve no longer counts; given a proposal is sent back or withdrawn, exposure is identical to before.
- Rules: BR-22, BR-24, BR-35. Touches: RP `GET /dashboard/exposure`; FE exposure view. (The "in settlement" figure is T2B.)

**US-18 [TRAIL] Every action writes exactly one entry, immutably** (MUST, T1)
As an auditor, I want one trail entry per business action written with the state change, never editable, so that history is dependable evidence.
- Given any command in BR-04, When it succeeds, Then entry count rises by exactly 1 with all mandatory fields (BR-05); a forced failure mid-command leaves neither state change nor entry (BR-07).
- Given an existing entry, When update or delete is attempted (API, and DB role if time), Then it fails; a mistake is fixed by a new Correction entry (BR-06, BR-74).
- Given I am the assigned officer or any Manager, When I issue a Correction with the id of one earlier entry and a reason of at least 10 characters, Then exactly one Internal Correction entry is added (any claim status, including final), the original is unchanged, and status, reserve, paid, exposure and metrics do not change.
- Given the Correction is issued by a Claimant, System, or an Officer not assigned to the claim, or has no reason or no entry id, Then 403 (or 422) and no Correction exists; the claimant timeline never shows a Correction, a count or a gap.
- Rules: BR-04 to BR-07, BR-10, BR-74. Touches: CS (history + outbox, one transaction); tests.

**US-20 [TRAIL] Visibility and actor are enforced server-side** (MUST, T1)
As a claimant and as an auditor, I want staff-only notes never sent to claimants and actors taken from the token, so that privacy and attribution hold.
- Given the claimant endpoint, When the response is inspected, Then no Internal entry and no field naming one (including proposal, maker, checker, send-back). Another claimant's claim id gives 403/404.
- Given a request body with a different actorId, Then the entry records the token identity; system actions record role System (BR-11).
- Rules: BR-09, BR-11, BR-12, BR-33. Touches: CS, GW; security tests.

### Tier 1 enablers (platform, MUST)
**US-23 Role-based mock login** (MUST, T1): As any user, I want to log in as a seeded claimant, officer or manager, so that I see my area. Given valid seeded credentials, Then I get a JWT and my role's area; a wrong role's area redirects or returns 403. Seed includes one user holding both Officer and Manager roles (to demo BR-32). Rules: BR-02. Touches: ID, GW, FE guards.
**US-24 Events drive reports** (MUST, T1): As a manager, I want reporting fed by claim events, so that dashboards match the trail. Given an event delivered twice, Then Reporting updates once. Touches: CS outbox, Kafka `claims.events`, RP.
**US-25 Run it locally** (MUST, T1): As a reviewer, I want `docker compose up` to start everything with seeded users, policies (with excess and coverage limit) and about 30 claims, so that I can follow the demo. Touches: all services, README.

---

# Tier 2A (SHOULD): claim-handling completeness

**US-07 Request information** (SHOULD, T2A; first to pull in)
As the assigned officer, I want to ask the claimant a question, so that I can evaluate fairly.
- Given UnderReview, When I request info with a question, Then InfoRequested, due date = request + 14 days, and a Public entry shows the question. Given an open request, Then 409; empty question, then 422; given a Pending* claim, then 409 (BR-15).
- Rules: BR-13 to BR-16. Touches: CS `POST /claims/{id}/info-requests`; FE claim detail.

**US-04 Answer an information request** (SHOULD, T2A)
As a claimant, I want to answer the officer's question, so that my claim moves on.
- Given InfoRequested, When I respond, Then UnderReview with the same officer and a Public "Information received". Given any other status, Then 409 and no entry.
- Rules: BR-01, BR-13 to BR-15. Touches: CS `POST /claims/{id}/info-responses`; FE respond form.

**US-22 [TRAIL] Refused attempts are trailed** (SHOULD, T2A)
As a manager, I want invalid or unauthorised attempts recorded internally, so that misuse is visible.
- Given an officer tries to propose settlement on an UnderReview claim, a maker tries to confirm own proposal, or a confirm loses a race, Then 409/403, no state change, and exactly one Internal "Refused" entry naming the actor and the attempt.
- Given an unauthenticated call, Then no entry is written.
- Given a refused Correction attempt (BR-74), Then the same Internal "Refused" entry is written.
- Rules: BR-01, BR-04, BR-32, BR-38, BR-74. Touches: CS.

**US-31 [MC][TRAIL] Withdraw my proposal** (SHOULD, T2A)
As the maker, I want to withdraw my pending proposal with a reason, so that I can correct it myself.
- Given my PendingDecisionApproval proposal, When I withdraw with a reason, Then UnderReview, one Internal entry (reason, proposalId); claimant sees nothing new. Given I am not the maker, Then 403. Given the checker confirmed first, Then 409.
- Rules: BR-02, BR-10, BR-36, BR-38. Touches: CS `.../withdraw`; FE claim detail.

**US-12 Manager reassigns a claim** (SHOULD, T2A)
As a manager (or the current officer, release), I want to reassign a claim with a reason, so that workload is balanced.
- Given UnderReview, InfoRequested or Pending*, When I reassign with a reason, Then assignee changes and one Internal entry records from, to, reason, who. Given a Pending* claim, Then the proposal is auto-withdrawn, the claim returns to UnderReview (or Approved, T2B), and the entry records the auto-withdrawal (BR-37).
- Given no reason, Then 422; given a final claim, Then 409. The previous maker may check a later proposal by someone else (BR-37). The claim stays assigned and never re-enters the Submitted "oldest unassigned" view (BR-76).
- Rules: BR-10, BR-27, BR-32, BR-37. Touches: CS (endpoint is the architect's); FE claim detail.

**US-02 See my claims** (SHOULD, T2A): As a claimant, I want a list of my claims with status, so that I can open them. Given I own 2 claims, Then I see exactly those 2; another claimant's id gives 403/404. Rules: BR-12. Touches: CS `GET /claims/mine`; FE list. (Cut: merge into US-03 screen.)

**US-05 See the decision** (SHOULD, T2A): As a claimant, I want outcome, reason and approved amount in one place, so that I understand the result. Given Approved, Then "approved for {currency amount}"; Rejected, then code and text; while pending, only "Decision under review". Rules: BR-08, BR-17, BR-33. Touches: CS timeline; FE claim detail. (Mostly covered by US-03; cut.)

**US-10 My workload** (SHOULD, T2A): As an officer, I want my open claims by status with ageing, so that I prioritise. Given 3 assigned claims (1 Settled), Then open = 2 grouped by status with ageing in days; claims in Pending* show as "waiting on checker". Rules: BR-28. Touches: RP `GET /staff/workload`; FE workload.

---

# Tier 2B (COULD, NOT approved for 5h): payment, ledger, pool fund

Everything below depends on Tier 1 and on each other in listed order. Money amounts are net of excess (BR-19).

**US-32 Claimant submits payee details after approval** (COULD, T2B)
As a claimant with an Approved claim, I want to give my bank details, so that I can be paid.
- Given Approved, When I submit payee name, bank, account number and bank code, Then they are stored in the Finance service only (never in Claims) with format validation, one Internal entry ("Payee details provided") is added, and the account number is masked (last 4) in every API response, entry, event and log.
- Given another claimant's claim or an officer calls it, Then 403. Given bad format, Then 422.
- Rules: BR-42, BR-04. Touches: PY (Finance); CS stores at most a payee-on-file flag; FE claimant claim detail.

**US-33 Claimant sees payable, paid and remaining limit** (COULD, T2B)
As a claimant, I want to see what is payable, what has been paid and my remaining coverage limit, so that I know where my money stands (I have no wallet).
- Given Approved with payable 7,500 and nothing paid, Then I see payable 7,500, paid-to-date 0, remaining = coverage limit.
- Given a payment completed, Then paid-to-date rises and remaining = limit minus paid-to-date.
- Given a payment Held or failed, Then my response contains no fund field, no Held reason, no failure (status stays Settling).
- Rules: BR-41, BR-72. Touches: PY/CS read; FE claim detail.

**US-34 Claimant money alerts** (COULD, T2B)
As a claimant, I want alerts on approval, dispatch and payment, so that I do not phone in.
- Given approval, Then "Claim approved: please provide your bank details". Given first dispatch, Then "Payment on the way" once per payment. Given completion, Then "Paid" with amount, currency and reference once.
- Given three failed attempts, Then the claimant gets no alert.
- Rules: BR-64, BR-08. Touches: NT, FE inbox.

**US-35 [MC] Request settlement (maker)** (COULD, T2B)
As the assigned officer, I want to request settlement of an Approved claim, so that a manager can confirm payment.
- Given Approved with payee details on file, When I request settlement, Then PendingSettlementApproval, Internal entry (makerId, proposalId), Public "Settlement under review". The request appears in the manager queue (US-30).
- Given no payee details, Then 422; given payee name differs from policyholder or nominee without justification, Then 422 (BR-43); given any other status, Then 409.
- Rules: BR-20, BR-42, BR-43, BR-36. Touches: CS; FE claim detail.

**US-36 [MC] Confirm or send back settlement (checker)** (COULD, T2B)
As a manager who is not the maker, I want to confirm a settlement, so that the payment is created.
- Given PendingSettlementApproval, When I confirm, Then Settling, reserve 0, payable frozen, exactly one payment instruction created, one Internal entry naming maker and checker. The claim is NOT Settled and no "settled" Public entry exists yet.
- Given confirm twice or concurrently, Then one payment only. Given I am the maker or Officer-only, Then 403. Send-back with a reason returns the claim to Approved.
- Rules: BR-20, BR-22, BR-31 to BR-34, BR-38, BR-41, BR-44. Touches: CS, PY.

**US-37 Payment executes; Settled only on PaymentCompleted** (COULD, T2B)
As a claimant, I want my claim marked Settled only when the bank actually paid, so that "settled" is true.
- Given a Settling claim and the simulated gateway set to succeed, When the payment completes, Then PY emits PaymentCompleted; CS records paid = payable, moves Settling to Settled (actor System) with one Public "Claim settled: {amount} paid, reference {ref}".
- Given a duplicate PaymentCompleted, Then nothing changes. Given a payment still Pending, Processing or Failed, Then the claim is not Settled.
- Rules: BR-45, BR-46, BR-48, BR-22. Touches: PY, Kafka, CS consumer.

**US-38 Payment failure, retry and exceptions queue** (COULD, T2B)
As a manager, I want failed payments queued after automatic retries, so that none is lost.
- Given the gateway always fails, Then exactly 4 attempts in total (1, 5, 30 minute backoff), each failure one Internal entry, then payment Failed, one manager alert, entry in the exceptions queue (oldest first, with reason and attempt count); claim stays Settling.
- Given a timeout then success, Then one payout only (same idempotency key).
- Rules: BR-45, BR-47, BR-65. Touches: PY, NT; FE manager queue.

**US-39 Manager resolves a failed payment: retry or correct payee** (COULD, T2B)
As a manager, I want to retry a failed payment or correct the payee details, so that the claimant gets paid.
- Given Failed, When I retry, Then a new cycle starts with a new attempt number and one Internal entry. When I correct payee details with a reason, Then a new revision is stored in Finance (previous kept; write-only: the manager can submit new details but no API returns the full account number) and the payment retried.
- Given an Officer or Claimant calls either, Then 403.
- Rules: BR-49, BR-02. Touches: PY; FE manager queue.

**US-40 Manager cancels a payment** (COULD, T2B)
As a manager, I want to cancel a Pending, Held or Failed payment with a reason, so that a wrong payment does not go out.
- Given such a payment, When I cancel with a reason, Then payment Cancelled, ledger reversed, reserve restored to the payable, claim returns Settling to Approved, one Internal entry.
- Given Processing or Paid, Then 409.
- Rules: BR-50, BR-22, BR-55 rule 9. Touches: PY, CS, LG.

**US-41 Funds check and held payment** (COULD, T2B)
As a manager, I want a payment held when the fund cannot cover it, so that the fund never goes negative.
- Given available funds (fund balance minus in transit) 5,000 and a payment of 7,500, Then payment Held "insufficient funds", nothing is posted, claim stays Settling, manager alerted, no retry consumed.
- Given a top-up of 10,000, Then the Held payment is released once, oldest first.
- Rules: BR-67, BR-65. Touches: PY, PF, LG.

**US-42 Fund top-up** (COULD, T2B)
As a manager, I want to top up a market's pool fund with a reason, so that payments can proceed.
- Given a positive amount, reason and request id, When I top up, Then exactly one balanced ledger entry (Dr 1000 / Cr 3000); a repeated request id posts nothing.
- Given an Officer or Claimant, Then 403.
- Known gap (accepted, documented): a top-up has no maker-checker; the reason, request id and manager are recorded and trailed instead.
- Rules: BR-68, BR-69, BR-55 rule 10. Touches: PF, LG; FE manager fund screen.

**US-43 Fund balance and coverage ratio view** (COULD, T2B)
As a manager, I want each fund's balance, earmarked amounts and coverage ratio, so that I see if we can pay what we owe.
- Given six funds, Then I see per currency: balance (= ledger 1000), reserve + payable + in transit, ratio; ratio below 1.0 is flagged. A low-balance threshold alert fires once when available falls below 20% of opening (BR-70).
- Given an Officer or Claimant, Then 403.
- Rules: BR-66, BR-70, BR-71, BR-62. Touches: PF, LG; FE.

**US-44 Ledger view and trial balance (Manager only)** (COULD, T2B)
As a manager, I want to see journal entries, account balances and a trial balance, so that I can verify the books.
- Given posted events, Then each entry is balanced, linked to claimId, eventId and trail entry id; the trial balance nets to zero per currency (red if not).
- Given the same event delivered 10 times, Then one entry. Given an Officer, Then 403. No edit or delete exists.
- Rules: BR-54 to BR-62. Touches: LG; FE read-only ledger.

**US-45 Reconciliation breaks view** (COULD, T2B)
As a manager, I want to see differences between payments, the bank statement and the ledger, so that errors are caught.
- Given the mock statement has one tampered amount, When reconciliation runs (on demand), Then one break is listed (missing, mismatch or unknown line) and a manager alert is raised.
- Given a Claims-side reserve differing from ledger 2000, Then it is listed too (BR-61).
- Rules: BR-53, BR-61, BR-65. Touches: PY, LG; FE manager.

---

# Tier 3 (COULD): nice-to-have

**US-11 Team performance** (COULD, T3): As an officer or manager, I want per-officer open/closed counts and average handling time, so that load is visible. Given decided claims, Then average handling time = first Submitted entry to confirmed Approved/Rejected entry (pending-approval time included, settlement excluded). Rules: BR-28, BR-29. Touches: RP.
**US-16 [TRAIL] Time in each status** (COULD, T3): As a manager, I want time per status of a claim, so that I see where it stalls. Given a decided claim, Then durations per status (Pending* included) sum to handling time. Rules: BR-29, BR-30. Touches: RP.
**US-17 Near-real-time refresh** (COULD, T3): As a manager, I want the dashboard to refresh within 10 seconds of an event (SSE or polling), so that I do not reload. Touches: RP `/events/stream`; FE.
**US-47 [MC] Checker turnaround and send-back rate** (COULD, T3): As a manager, I want proposal-to-confirm time and the send-back rate per officer, so that control effectiveness is visible. Given a proposal and its confirm, Then turnaround = gap of the two entries matched by proposalId; withdrawals excluded. Rules: BR-40, BR-39. Touches: RP.
**US-48 Manager money alerts and bank reversal** (COULD, T3, only after T2B): As a manager, I want alerts for payment reversed by bank, ledger out of balance and fund below minimum, so that I act. Given a bank-returned Paid payment, Then status Reversed, Internal entry, ledger reversed, one alert; the claim stays Settled. Rules: BR-52, BR-65. Touches: PY, NT.

---

## Counts and demo
| Tier | Priority | Stories |
|---|---|---|
| T1 | MUST | 13 (US-01, 03, 06, 09, 13, 14, 18, 20, 26, 27, 28, 29, 30) + 3 enablers (US-23, 24, 25) |
| T2A | SHOULD | 8 (US-02, 04, 05, 07, 10, 12, 22, 31) |
| T2B | COULD | 14 (US-32 to US-45) |
| T3 | COULD | 5 (US-11, 16, 17, 47, 48) |
Total 43 (40 stories + 3 enablers). The 11 core trail and maker-checker stories are US-01, 03, 06, 09, 18, 20, 26, 27, 28, 29, 30; US-13 and US-14 are the brief's manager asks.
Demo (Tier 1, under 5 min): claimant submits; officer picks up, assesses, proposes approve; claimant sees only "Decision under review"; the dual-role user tries to confirm own proposal and gets 403; manager sends back, officer re-proposes, manager confirms; claimant sees "approved"; officer view shows Internal entries with maker and checker; manager dashboard updates. Demo ends at Approved. Tier 2B demo (if ever built) adds payee details, settlement, a failing then succeeding payment, a held payment and a top-up, and the trial balance.

## Confirmed decisions
Business analyst (BR-74 to BR-77 and edits to BR-04/06/08/10/28):
- Correction entry: Internal only; assigned officer or any Manager; mandatory reason (min 10 chars) and the corrected entry id; no state or money change (US-18, US-22).
- "Claim received" is the single submission entry: actor Claimant, Public, text "Claim received: {claimNumber}" (US-01, US-03).
- "Oldest unassigned" = Submitted with no assignee, age from submittedAt in calendar time, sorted by submittedAt then claim number, never re-entered by reassignment or auto-withdraw (US-13, US-12).
- Outstanding = every non-Rejected, non-Settled claim counted once under its own status; Pending* are separate rows; optional "awaiting checker" subtotal (US-13).
- Accepted assumptions: auto-withdraw on reassignment (BR-37); claimant sees only "Decision under review" after a send-back (BR-33); checker confirms or sends back, never edits (BR-34); excess is one fixed amount per policy (BR-19).
Human: Finance is one service with three modules (Phase 2); payee details live in Finance only, with write-only correction; dev JWT signing is RS256; top-ups have no maker-checker (documented gap, US-42).
Product owner (scope): only Tier 1 is committed for the 5h cap and the demo ends at confirmed Approved/Rejected; Tier 2B is "designed, not built" (the P1/P2 boundary is settled in TASKS and ARCHITECTURE section 0). US-13/14 stay in Tier 1 as plain tables because the brief asks for them. "Real-time" means visible within 10 seconds (SSE or polling, Tier 3). Team performance (US-11) is visible to officers and managers. Earlier human settlements: manager reassigns; settlement excluded from handling time; amounts net of excess; refused attempts trailed Internal; 14-day deadline and six reject codes; maker-checker scope and roles; Settling state.

## Assumptions needing human confirmation
Phase 2 only, no effect on the 5h build; confirm before Finance is built: manager cancel returns Settling to Approved with reserve restored (BR-50); accounting simplification, expense at payable raised (BR-54/55); retry backoff 1, 5, 30 minutes, four attempts (BR-47); payee name must match policyholder or nominee unless justified (BR-42/43); bank-reversed payment leaves the claim Settled (BR-52); seeded fund amounts, 20% warning threshold, coverage ratio red below 1.0, claimant sees payable / paid-to-date / remaining limit only (BR-66 to BR-72).

## Traceability (stories to current TASKS)
BE = `ChubbBackEnd/docs/planning/TASKS.md` (section number); FE = `ChubbFrontEnd/docs/planning/TASKS.md` (section number). Every active story maps to at least one task. "n/a" = no UI/service work by design.
| Story | Backend task (BE) | Frontend task (FE) |
|---|---|---|
| US-01 | 4.4 `POST /claims`; 4.1 claim number, validation, entities; 4.2 one entry per action ("Claim received") | 3 Report incident form |
| US-02 | 4.4 `GET /claims/mine` | 3 My claims list |
| US-03 | 4.2 timeline, claimant masking, final entries | 3 Claim detail with timeline; 2 shared timeline component |
| US-04 | 4.4 `info-responses` | 3 Respond to information request |
| US-05 | 4.2 claimant final entries | 3 See the decision |
| US-06 | 4.4 Officer `GET /claims`, assign | 4 Incoming queue and pick-up |
| US-07 | 4.4 `info-requests` | 4 Request information |
| US-09 | 4.2 timeline (staff), manager opens any claim | 4 Claim detail full trail; 5 Manager claim trail view; 2 shared component |
| US-10 | 6 `GET /staff/workload` | 4 My workload |
| US-11 | 6 Team performance (COULD) | 4 Team table |
| US-12 | 4.3 reassign; 4.4 reassign endpoint | 4 Release/reassign; 5 Reassign a claim |
| US-13 | 6 `GET /dashboard/outstanding` (BR-76/77) | 5 Outstanding claims dashboard |
| US-14 | 6 `GET /dashboard/exposure` | 5 Liability exposure |
| US-16 | 6 Team performance/time in status (COULD) | 5 Charts, turnaround, time in status |
| US-17 | 6 SSE `/events/stream` | 5 Refresh via SSE/polling |
| US-18 | 4.2 append-only history, one transaction, immutability, Correction (BR-74), one entry per action; 8 Trail tests | n/a (server-side); 7 security tests |
| US-20 | 2 security gate; 4.4 DTOs expose only client-settable fields; 3 Gateway; 8 S1 to S5 | 2 JWT interceptor, Gateway-only; 7 Security tests |
| US-22 | 4.2 Refused attempts (incl. refused Correction) | 4 Display refused attempts |
| US-23 | 3 Identity, Gateway | 2 Auth service, guards, lazy areas |
| US-24 | 5 Kafka and contracts; 4.5 Outbox; 6 idempotent consumer | n/a (backend only) |
| US-25 | 1 scaffold and README; 9 Local run | 2 Scaffold, API client; 6 README, Playwright smoke |
| US-26 | 4.1 Assessed amount; 4.4 set assessed | 4 Assess |
| US-27 | 4.3 Propose decision; 4.4 `decision-proposals` | 4 Propose decision form |
| US-28 | 4.3 Confirm, maker cannot check; 4.4 `confirm` | 5 Pending-approval queue (Confirm) |
| US-29 | 4.3 Send back; 4.4 `send-back` | 5 Pending-approval queue (Send back) |
| US-30 | 6 `GET /dashboard/pending-approvals`; 7 manager alert | 5 Pending-approval queue |
| US-31 | 4.3 Withdraw; 4.4 `withdraw` | 4 Withdraw my pending proposal |
| US-32 | P2-A Payee details (stored in Finance only) | P2 Mockup payee form |
| US-33 | P2-A Claimant money view | P2 Mockup money view |
| US-34 | P2-E Money alerts; 7 (P2) | P2 Mockup money alerts inbox |
| US-35, US-36 | P2-A Request / confirm settlement | P2 Mockup settlement screens |
| US-37 | P2-A Settled on PaymentCompleted; 4.5 `finance.events` consumer | n/a (claimant sees Public entry in shared timeline) |
| US-38 | P2-B Failure handling | P2 Mockup exceptions queue |
| US-39, US-40 | P2-B Retry / correct payee (write-only) / cancel | P2 Mockup exceptions queue |
| US-41, US-42, US-43 | P2-D Funds check, top-up (no maker-checker, documented gap), balance and ratio | P2 Mockup pool fund |
| US-44 | P2-C Ledger and trial balance | P2 Mockup ledger |
| US-45 | P2-C Reconciliation | P2 Mockup reconciliation |
| US-47 | 6 Team performance (COULD) | 5 Charts, turnaround, send-back rate |
| US-48 | P2-B Bank return; P2-E manager alerts | n/a (alerts only, no screen) |

Tasks with no story (explicit "no story" in TASKS): scaffold extras, commits, journal, ADR upkeep, architecture sync, spec-first rule, ERD, `.env.example`, Kafka UI, health/Swagger, AsyncAPI, S6, document upload, pre-commit security hook, production-gaps list, screenshots fallback, mockup approvals record.
