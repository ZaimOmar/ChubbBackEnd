# ChubbBackEnd - Tasks

Owner: Product Owner. Master list: `../Assessment/AssessmentTasks.md`. Frontend list: `../ChubbFrontEnd/docs/planning/TASKS.md`.
Sources of truth (not edited here): `docs/product/user-stories.md`, `docs/domain/business-rules.md`, `docs/architecture/ARCHITECTURE.md` (section 0 phase boundary, section 11 cut line), `docs/architecture/MODULES.md`, `docs/security/SECURITY-REQUIREMENTS.md`, `docs/adr/`.

**Format:** `- [ ] [PRIORITY][PHASE] task - stories; rules`. Priority: MUST / SHOULD / COULD. Phase: **P1** = built within the 5h cap, **P2** = designed, not built (kept visible in the architecture as an enhancement).
**Only the ADR items are ticked (the files exist); everything else is unticked.**

## Phase boundary (settled)
- **P1 (build):** Gateway, Identity, Claims (9-state machine, claim progress trail, maker-checker on the DECISION, refused attempts trailed, reassignment), Reporting, Notification. Demo ends at Approved or Rejected. Finance is NOT in docker-compose.
- **P2 (designed, not built):** Finance service (Payment, General Ledger, Pool Fund modules), settlement maker-checker, Settling, payee details, money alerts, reconciliation. Six services in the full design.
- Focus of the assessment: the claim progress TRAIL (US-03, 09, 18, 20, 22, 27 to 29).
- Settlement, Settling and Settled exist in the status enum but are disabled by flag `Settlement:Enabled=false` (409 `phase-2-not-enabled`).
- **Cut order (first cut first):** T3 (US-11, 16, 17, 47, 48) > all P2 > T2A (US-10, 12, 31, 02, 05, then 22, 04, 07) > inside T1: US-13/14 as plain tables, US-30 folded into a claim list filter, frontend polish. **Never cut:** US-01, 03, 06, 09, 18, 20, 26 to 29, ADRs, journal, compose.
- Realistic 5h stretch beyond T1: US-07, US-04 (info request and answer) and US-22 (refused attempts), because they enrich the trail.

## 1. Setup, docs, ADRs
- [ ] [MUST][P1] Solution scaffold: Gateway, Identity, Claims, Reporting, Notification, Contracts, tests (Finance project NOT created) - US-25; BR-01
- [ ] [MUST][P1] SPEC.md (from `docs/specs/SPEC-TEMPLATE.md`) written and architect-REVIEWED before coding each module/feature - process, no story
- [ ] [MUST][P1] Small conventional commits from the start (history is graded) - process, no story
- [ ] [MUST][P1] AI journal `docs/journal/AI-JOURNAL.md`, updated after each phase - process, no story
- [ ] [MUST][P1] README: run steps, seeded users and credentials, architecture diagram (mermaid), Phase 1 vs Phase 2, assumptions, limitations - US-25
- [ ] [MUST][P1] Keep docs/architecture/ARCHITECTURE.md, docs/architecture/MODULES.md and diagrams in sync with what was actually built (Phase 2 parts stay labelled) - no story
- [x] [MUST][P1] ADR-001 service split (six services, Finance one service with three modules) - docs/adr/ADR-001
- [x] [MUST][P1] ADR-002 REST for commands and queries, Kafka for events - docs/adr/ADR-002
- [x] [MUST][P1] ADR-003 contract-first OpenAPI and versioned event contracts - docs/adr/ADR-003
- [x] [MUST][P1] ADR-004 outbox, idempotent consumers, DLQ - docs/adr/ADR-004
- [x] [MUST][P1] ADR-005 maker-checker enforced in the domain - docs/adr/ADR-005; BR-31 to BR-39
- [x] [MUST][P2] ADR-006 payments, retries, Settling saga (written; Finance not built) - docs/adr/ADR-006
- [x] [MUST][P2] ADR-007 ledger design (written; Finance not built) - docs/adr/ADR-007
- [x] [MUST][P1] ADR-008 authentication and authorisation (mock RS256 JWT, roles) - docs/adr/ADR-008
- [ ] [SHOULD][P1] ADR: multi-market and currency handling (market code, currency, static dated FX) - US-01, 14; BR-23, BR-24
- [ ] [SHOULD][P1] ERD in mermaid (Phase 2 tables marked dashed); `.env.example`; Kafka UI in compose - no story

## 2. Security gate (applies to every slice)
- [ ] [MUST][P1] `architect`: threat model and authorisation matrix in `docs/security/SECURITY-REQUIREMENTS.md` confirmed before Claims is built - US-20; BR-02, BR-11
- [ ] [MUST][P1] `devops-engineer`: `scripts/security-check.sh` (gitleaks, dependency audit, image scan, format) exits 0 before each slice is accepted - US-20
- [ ] [MUST][P1] `qa-test-engineer`: security tests S1 to S16 implemented (see section 8) - US-20, 27 to 29; BR-02, BR-32
- [ ] [MUST][P1] Named authorisation policy on every endpoint, default deny; actor and role from the token only - US-20; BR-11
- [ ] [MUST][P1] No bank details stored or accepted anywhere in Phase 1 (nothing to mask yet) - BR-42
- [ ] [SHOULD][P1] Pre-commit hook or CI step runs the security check - no story
- [ ] [SHOULD][P1] Production gaps listed in README and walkthrough (mock JWT, no TLS, no Kafka ACLs, no KMS) - no story
- [ ] [COULD][P2] Security tests S17 to S31 (Finance and settlement) written alongside Phase 2 - US-32 to 45, 48; BR-42 to BR-72

## 3. Identity and Gateway
- [ ] [MUST][P1] Identity: seeded users per role (incl. a second claimant and a second officer for S2, S4, S10) plus one user holding Officer AND Manager (demos BR-32), dev login issuing RS256 JWT - US-23; BR-02, BR-32
- [ ] [MUST][P1] Gateway (YARP): routes for Identity, Claims, Reporting, Notification; JWT validation (signature, issuer, audience, expiry), CORS - US-23, 20; BR-02
- [ ] [MUST][P1] Aggregated OpenAPI from the Gateway for frontend client generation - US-25 (ADR-003)
- [ ] [SHOULD][P1] CorrelationId propagated across HTTP and Kafka headers and written to trail entries - US-18; BR-05
- [ ] [SHOULD][P1] Health endpoints, Swagger UI - no story
- [ ] [COULD][P2] Gateway routes `/payments`, `/funds`, `/ledger` present but commented out; internal reserve endpoint not exposed - US-32 to 45; BR-61

## 4. Claims service
### 4.1 Domain, state machine, data
- [ ] [MUST][P1] Entities: Claim (with nullable `payable`, `paid`, and at most a `payeeOnFile` flag reserved; payee details live in Finance only), Motor/Property details, Policy (with excess, coverage limit, holder name), InformationRequest, Proposal (generic, `kind = Decision | Settlement`), ClaimHistory - US-01, 26, 27; BR-04, BR-19, BR-21, BR-23
- [ ] [MUST][P1] State machine in Domain with all 9 states (Submitted, UnderReview, InfoRequested, PendingDecisionApproval, Approved, Rejected, PendingSettlementApproval, Settling, Settled); invalid transitions rejected (409/422) - US-01 to 06, 27 to 29; BR-01, BR-03
- [ ] [MUST][P1] Settlement transitions present in the table but gated by `Settlement:Enabled=false` returning 409 `phase-2-not-enabled`; unit test proves it - US-35 to 37 (P2); BR-01, BR-20
- [ ] [MUST][P1] Claim number `CLM-{MKT}-{YYYY}-{NNNNNN}`, claim currency from policy market, six markets - US-01; BR-23, BR-25
- [ ] [MUST][P1] Assessed amount command: sets assessed, derives net payable = max(assessed - excess, 0) and reserve, one Internal entry - US-26; BR-19, BR-21, BR-22, BR-36
- [ ] [MUST][P1] Reserve rules: reserve = estimate on submit, = payable on assessment and approval, 0 on rejection; no change while a proposal is pending - US-26 to 28; BR-22, BR-35
- [ ] [MUST][P1] EF migrations auto-applied; seed policies (excess, limit, holder) and about 30 claims across markets and statuses - US-25; BR-23
- [ ] [MUST][P1] Indexes on (status, market), assignee, createdAt; `rowVersion` optimistic concurrency - US-06, 28; BR-26, BR-38
- [ ] [MUST][P1] FluentValidation on commands; RFC 7807 problem details; pagination with max page size - US-01, 27; BR-17, BR-18
### 4.2 Claim progress trail (centrepiece)
- [ ] [MUST][P1] Append-only `claim_history`: id, claimId, occurredAt (server UTC), eventType, from/to status, actorId, actorRole, summary, reason, visibility Public|Internal, proposalId, correlationId - US-18; BR-04, BR-05, BR-39
- [ ] [MUST][P1] Entry, state change and outbox row written in ONE transaction; forced failure leaves none of the three - US-18; BR-07
- [ ] [MUST][P1] Immutability: no update or delete through API; DB role INSERT+SELECT only with trigger blocking UPDATE/DELETE; fix by `Correction` entry: Internal only, assigned officer or any Manager (else 403 + Refused entry), reason min 10 chars and exactly one referenced entry id, no state or money change - US-18, 22; BR-06, BR-74
- [ ] [MUST][P1] Exactly one entry per business action in BR-04 (submitted, assigned, reassigned, info events, assessed, proposed, confirmed, sent back, withdrawn, refused) - US-18; BR-04
- [ ] [MUST][P1] Submission writes the single Public entry "Claim received: {claimNumber}", actor Claimant from the token - US-01, 03; BR-04, BR-75
- [ ] [MUST][P1] `GET /claims/{id}/timeline`: claimant sees own claim, Public only, no Internal entry, count, gap or id; officer and manager see Public + Internal marked as such - US-03, 09, 20; BR-08, BR-09, BR-12
- [ ] [MUST][P1] Claimant masking while a decision is pending and after a send-back: only "Decision under review" - US-03, 29; BR-33
- [ ] [MUST][P1] Claimant final entries: rejection code and text; approval "approved for {currency amount}" - US-03, 05; BR-08, BR-17
- [ ] [MUST][P1] Manager can open any claim and read the full trail (replaces old manager detail gap) - US-09; BR-02, BR-12
- [ ] [SHOULD][P1] Refused attempts: wrong-state, maker-cannot-check, lost race each write exactly one Internal "Refused" entry naming actor and attempt; unauthenticated calls write nothing - US-22; BR-01, BR-04, BR-32, BR-38
- [ ] [SHOULD][P1] Reject reasons from the six-code list plus free text (min 10 chars) recorded in the trail - US-27; BR-10, BR-17
### 4.3 Maker-checker on the DECISION (domain-enforced)
- [ ] [MUST][P1] Propose decision (approve or reject) with rationale: UnderReview to PendingDecisionApproval; Internal entry (outcome, amount, rationale, makerId, proposalId) plus Public "Decision under review"; 422 on bad code, payable 0, no assessed amount, over-estimate without justification - US-27; BR-10, BR-17 to BR-19, BR-21, BR-33, BR-35, BR-36
- [ ] [MUST][P1] Confirm: manager who is not the maker; applies exactly as proposed (payload carries only `proposalId`); Approved (reserve = payable) or Rejected (reserve 0); entry names makerId and checkerId - US-28; BR-22, BR-31, BR-32, BR-34, BR-38, BR-39
- [ ] [MUST][P1] Maker can never check own proposal, even with both roles (403 `maker-cannot-check`); Officer-only gets 403; concurrent confirms yield one success, one 409 - US-28; BR-32, BR-38
- [ ] [MUST][P1] Send back with reason (min 10 chars): back to UnderReview, assignee unchanged, reserve unchanged - US-29; BR-10, BR-34, BR-35, BR-39
- [ ] [SHOULD][P1] Withdraw by the maker with reason; non-maker 403; confirm winning the race gives 409 - US-31; BR-02, BR-10, BR-36, BR-38
- [ ] [SHOULD][P1] Manager reassign with reason (also officer release); pending proposal auto-withdrawn and recorded; previous maker may check a later proposal by someone else - US-12; BR-10, BR-27, BR-32, BR-37
- [ ] [MUST][P1] Guard rules while Pending*: no info request, assessment or edit; stale `proposalId` gives 409 - US-27, 28; BR-15, BR-36
### 4.4 Endpoints (via Gateway)
- [ ] [MUST][P1] Claimant: `POST /claims` (US-01), `GET /claims/{id}` + timeline (US-03) - BR-04, BR-12, BR-25
- [ ] [SHOULD][P1] Claimant: `GET /claims/mine` (US-02), `POST /claims/{id}/info-responses` (US-04) - BR-12, BR-13 to BR-15
- [ ] [MUST][P1] Officer: `GET /claims` (filters market/type/status/assignee, paged), `POST /claims/{id}/assign` concurrency-safe (409 on conflict) - US-06; BR-01, BR-02, BR-26
- [ ] [SHOULD][P1] Officer: `POST /claims/{id}/info-requests` (due date = +14 days; one open request) - US-07; BR-13 to BR-16
- [ ] [MUST][P1] Officer: set assessed amount; `POST /claims/{id}/decision-proposals` - US-26, 27; BR-19, BR-21
- [ ] [MUST][P1] Manager: `POST /claims/{id}/decision-proposals/{pid}/confirm` and `.../send-back` - US-28, 29; BR-31 to BR-34
- [ ] [SHOULD][P1] Maker: `.../withdraw`; manager/officer reassign endpoint - US-31, 12; BR-27, BR-37
- [ ] [MUST][P1] REMOVED: single-actor `/decision` and `/settle` endpoints must not exist; settlement routes return 409 `phase-2-not-enabled` - US-27, 28; BR-31
- [ ] [MUST][P1] Request DTOs expose only client-settable fields (never status, assignee, reserve, approved amount, makerId, checkerId) - US-20; BR-11, BR-34
- [ ] [SHOULD][P1] Reference-data endpoint (markets, currencies, claim types, reject codes) - US-01, 27; BR-17, BR-23 (COULD if short)
### 4.5 Outbox
- [ ] [MUST][P1] Transactional outbox plus publisher background service; publishes every Phase 1 event incl. `ClaimAssessed` and `ClaimApproved` with `payable`, `currency`, `proposalId` - US-24, 18; BR-07
- [ ] [COULD][P2] Claims consumer for `finance.events` (reusing `processed_events` and DLQ plumbing) enabling Settling and Settled - US-37; BR-45, BR-48

## 5. Kafka and contracts
- [ ] [MUST][P1] Topics `claims.events` (key = claimId) and `claims.events.dlq`; topic init in compose - US-24; ADR-004
- [ ] [MUST][P1] Versioned event envelope (eventId, type, version, occurredAt, claimId, market, payload) and all Phase 1 event types in `Contracts` (submitted, assigned/reassigned, info requested/provided, assessed, decision proposed/confirmed/sent back/withdrawn, approved, rejected) - US-24; BR-04
- [ ] [SHOULD][P1] Retry with backoff then DLQ; separate consumer groups per consumer; document the event schema - US-24 (S8)
- [ ] [COULD][P1] AsyncAPI doc - no story
- [ ] [COULD][P2] `finance.events` topic + dlq and Finance event contracts (SettlementConfirmed, PaymentCompleted etc.) kept in design only - US-36, 37; BR-44, BR-48

## 6. Reporting service
- [ ] [MUST][P1] Idempotent consumer (`processed_events`) projecting into read-model tables; duplicate event updates once - US-24; BR-28
- [ ] [MUST][P1] `GET /dashboard/outstanding`: counts by status (Pending* separate), market, type; oldest unassigned = Submitted with no assignee by submittedAt then claim number, calendar time; every non-Rejected non-Settled claim counted once under its own status, optional awaiting-checker subtotal - US-13; BR-28, BR-76, BR-77
- [ ] [MUST][P1] `GET /dashboard/exposure`: by market and type in USD, static dated FX; Pending* at current reserve; Rejected excluded; sent-back/withdrawn unchanged - US-14; BR-22, BR-24, BR-35
- [ ] [MUST][P1] `GET /dashboard/pending-approvals`: manager checker queue oldest first (claim number, market, type, amount, maker, age); Officer 403 - US-30; BR-02, BR-28
- [ ] [SHOULD][P1] `GET /staff/workload`: open per officer by status with ageing, Pending* shown as waiting on checker - US-10; BR-28
- [ ] [COULD][P1] Team performance and handling time (first Submitted to confirmed decision, settlement excluded), time in status, checker turnaround and send-back rate - US-11, 16, 47; BR-29, BR-30, BR-40
- [ ] [COULD][P1] SSE `/events/stream` for dashboard refresh within 10 seconds (or frontend polling) - US-17; BR-28
- [ ] [COULD][P2] "In settlement" exposure figure and ledger tie-out (exposure = ledger 2000) - US-14, 44; BR-61

## 7. Notification service
- [ ] [MUST][P1] Idempotent consumer logging/stubbing notifications on assignment, decision under review (manager), approval and rejection - US-24, 03; BR-08, BR-64
- [ ] [SHOULD][P1] Claimant inbox endpoint with Public-only content; alert on info request and answer; no pending-decision outcome leaked - US-03, 07; BR-14, BR-33
- [ ] [SHOULD][P1] Manager alert when a proposal awaits check - US-30; BR-65
- [ ] [COULD][P2] Money alerts: approved-provide-bank-details, payment on the way, paid; manager alerts for failed, reversed, low fund, out-of-balance - US-34, 48; BR-64, BR-65, BR-70

## 8. Testing
- [ ] [MUST][P1] Unit tests: state machine (all 9 states, flag-disabled settlement), maker-checker rules, net-of-excess and reserve rules, exposure calculation, assignment - US-26 to 29; BR-01, BR-19, BR-22, BR-32
- [ ] [MUST][P1] Trail tests: one entry per action; entry + state + outbox atomic; update/delete fail; claimant never receives Internal data - US-18, 20; BR-04 to BR-07, BR-09
- [ ] [MUST][P1] API integration tests (Testcontainers): role authorisation, concurrent pick-up, concurrent confirms - US-06, 28; BR-26, BR-38
- [ ] [MUST][P1] S1 token invalid/expired/wrong audience (401) - US-20
- [ ] [MUST][P1] S2 claimant A reads claimant B's claim (403/404) - US-02, 20; BR-12
- [ ] [MUST][P1] S3 wrong-role endpoint calls (403) - US-20; BR-02
- [ ] [MUST][P1] S4 non-assigned officer decides (403/409) - US-26, 27; BR-02
- [ ] [MUST][P1] S5 server-owned fields in body ignored/400 - US-20; BR-11
- [ ] [MUST][P1] S6 RFC 7807, no stack trace, no PII in logs - no story
- [ ] [MUST][P1] S7 oversized body, page size, absurd amount - US-01
- [ ] [MUST][P1] S8 malformed/wrong-version/duplicate Kafka events - US-24
- [ ] [MUST][P1] S9 invalid transitions 409/422 - US-27, 28; BR-01
- [ ] [MUST][P1] S10 concurrent pick-up one 200 one 409 - US-06; BR-26
- [ ] [MUST][P1] S11 dual-role user confirms own proposal: 403 `maker-cannot-check`, 403, no state change (Refused entry once US-22 is built) - US-28, 22; BR-32
- [ ] [MUST][P1] S12 Officer-only confirm/send-back 403 - US-28, 29; BR-02
- [ ] [MUST][P1] S13 non-assigned officer proposes/withdraws; non-maker withdraws 403 - US-27, 31; BR-02
- [ ] [MUST][P1] S13b claimant timeline during pending and after send-back - US-03, 29; BR-33
- [ ] [MUST][P1] S14 stale `proposalId` confirm 409 - US-28; BR-38, BR-39
- [ ] [MUST][P1] S15 simultaneous confirms / confirm vs withdraw: one wins - US-28, 31; BR-38
- [ ] [MUST][P1] S16 confirm request with amount/outcome ignored - US-28; BR-34
- [ ] [SHOULD][P1] Consumer idempotency test; Kafka/outbox/DLQ Testcontainers test - US-24; BR-07
- [ ] [COULD][P1] End-to-end smoke across services - US-25
- [ ] [COULD][P2] S17 to S31 and Finance unit tests (payment states, balanced journal, idempotent posting) - US-32 to 45, 48; BR-42 to BR-72

## 9. Local run
- [ ] [MUST][P1] `docker-compose.yml`: Postgres (4 databases: identity, claims, reporting, notification), Kafka KRaft single broker, topic init, Gateway, Identity, Claims, Reporting, Notification. NO Finance service, no `finance_db`; `docker compose up` works with zero manual steps - US-25
- [ ] [MUST][P1] Migrations and seed on startup: users (incl. dual-role), policies with excess and coverage limit, about 30 claims in varied statuses incl. some PendingDecisionApproval - US-25; BR-19, BR-23
- [ ] [MUST][P1] README: seeded credentials, run-without-Docker, how to regenerate the OpenAPI client - US-25
- [ ] [COULD][P2] Finance container, `finance_db`, chart of accounts and six funds with opening entries added to compose - US-42, 43; BR-54, BR-66

## 10. Walkthrough prep
- [ ] [MUST][P1] Demo script (under 5 min): claimant submits; officer picks up, assesses, proposes approve; claimant sees only "Decision under review"; dual-role user confirms own proposal and gets 403; manager sends back; officer re-proposes; manager confirms; claimant sees "approved"; officer sees full trail with maker and checker; manager dashboards update. Ends at Approved - US-01, 03, 06, 09, 13, 14, 26 to 29
- [ ] [MUST][P1] Be ready to justify: why services for this size, why Kafka for these events, trail written atomically with outbox, maker-checker in domain, concurrency-safe pick-up, exposure definition, scaling to six markets - US-18, 24, 28; BR-22, BR-32
- [ ] [MUST][P1] Say plainly what is built (Phase 1) vs designed (Phase 2) and why Finance was deferred under the 5h cap - ARCHITECTURE section 0, 11
- [ ] [MUST][P1] "With more time" list: Finance (F1 payment + settlement maker-checker, F2 funds/Held/top-up, F3 cancel/reversal/reconciliation), OIDC, KMS, Kafka ACLs, schema registry, CI, charts - ARCHITECTURE section 11
- [ ] [SHOULD][P1] Screenshots and seeded state as fallback if Kafka fails - no story

---

# PHASE 2 (designed, NOT built): Finance, settlement maker-checker

Nothing here is scheduled in the 5h cap. Order when it starts (ARCHITECTURE section 11): F1, then F2, then F3. Rules, stories, ADR-006/007 and the worked example already exist, so the demo states "designed, not built".

## P2-A Claims side of settlement (enable behind flag)
- [ ] [COULD][P2] Payee details submit by the claimant after Approved (masked last 4 everywhere) - US-32; BR-42, BR-43, BR-04
- [ ] [COULD][P2] Request settlement (maker): Approved with payee on file to PendingSettlementApproval; Public "Settlement under review" - US-35; BR-20, BR-36, BR-42, BR-43
- [ ] [COULD][P2] Confirm / send back settlement (checker): Settling, reserve 0, payable frozen, one payment instruction; send-back returns to Approved - US-36; BR-20, BR-22, BR-31 to BR-34, BR-38, BR-41, BR-44
- [ ] [COULD][P2] Settled only on `PaymentCompleted`; duplicate ignored; Public "Claim settled" - US-37; BR-45, BR-46, BR-48, BR-22
- [ ] [COULD][P2] Claimant money view: payable, paid-to-date, remaining coverage limit; no fund/Held/failure fields - US-33; BR-41, BR-72
- [ ] [COULD][P2] Settlement items join the manager pending-approvals queue - US-30, 35, 36; BR-28
## P2-B Finance - Payment module
- [ ] [COULD][P2] Payment instruction, simulated bank gateway (Development only), idempotency key per attempt, unique active payment per claim - US-36, 37; BR-44, BR-45; ADR-006
- [ ] [COULD][P2] Failure handling: 4 attempts total (1, 5, 30 min backoff), then Failed, manager alert, exceptions queue oldest first - US-38; BR-45, BR-47, BR-65
- [ ] [COULD][P2] Manager retry / correct payee (new revision, history kept) / cancel Pending, Held or Failed (Processing/Paid 409) - US-39, 40; BR-49, BR-50, BR-22
- [ ] [COULD][P2] Bank return of a Paid payment: Reversed, claim stays Settled, alert - US-48; BR-52, BR-65
## P2-C Finance - General Ledger module
- [ ] [COULD][P2] Chart of accounts per currency; posting rules; balanced, append-only, idempotent journal keyed on source eventId; traceability to claimId and trail entry - US-44; BR-54 to BR-59; ADR-007
- [ ] [COULD][P2] Trial balance and ledger view (Manager only), nets to zero per currency - US-44; BR-60, BR-62
- [ ] [COULD][P2] Reconciliation: payments vs mock bank statement vs ledger; Claims reserve vs ledger 2000 via internal route (role `Service`, not on Gateway) - US-45; BR-53, BR-61
## P2-D Finance - Pool Fund module
- [ ] [COULD][P2] Funds check before dispatch under per-currency advisory lock; insufficient funds gives Held, no retry consumed, release oldest first after top-up - US-41; BR-67, BR-65
- [ ] [COULD][P2] Manager fund top-up (Dr 1000 / Cr 3000), idempotent by request id - US-42; BR-68, BR-69
- [ ] [COULD][P2] Fund balance, earmarked amounts, coverage ratio (red below 1.0), low-balance alert once below 20% - US-43; BR-66, BR-70, BR-71, BR-62
## P2-E Alerts, topics, deployment
- [ ] [COULD][P2] Claimant and manager money alerts - US-34, 48; BR-64, BR-65
- [ ] [COULD][P2] `finance.events` topic, Claims consumer, Gateway routes, compose entry, ERD tables - US-37; ARCHITECTURE section 11

---

## Documented gaps (from the story traceability review)
Stories with no Phase 1 task by design: US-32 to US-45 and US-48 (all Phase 2, listed above). Stories now covered that previously had none: US-26 to 31, US-12, US-22, US-47 (sections 4 and 6).
Tasks with no story (technical or process, kept on purpose): scaffold, ADRs, journal, commits, ERD, Kafka UI, `.env.example`, health/Swagger, CorrelationId, AsyncAPI, S6, walkthrough items.
Removed as stale: single-actor `/decision` and `/settle`, "paid on settle" reserve handling, five-service wording, "seven event types", SLA-breach flags, "document metadata only", "payment execution out of scope" (it is Phase 2, designed), ledger-as-reserve leftovers.
Closed by the BA (BR-74 to BR-77) and the human: Correction, "Claim received", oldest unassigned, outstanding counting, assumptions BR-37/33/34/19, Finance as one service, payee in Finance only, RS256, top-ups without maker-checker (documented gap). Still open (Phase 2 only): the Phase 2 assumptions listed in docs/product/user-stories.md.
Changed in this pass: added the spec-first process task; Claims entity no longer reserves `payeeName` (payee in Finance only); Correction, "Claim received", oldest-unassigned and outstanding wording aligned to BR-74 to BR-77; S11 no longer depends on the SHOULD story US-22.
Priority notes: trail tests (US-18/20) are now MUST; Reporting items are MUST but depend on Kafka (US-24).
