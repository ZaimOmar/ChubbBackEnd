# Security Requirements – Chubb Claims Platform

Status: **DRAFT**. Owner: `architect`. Written before Claims and Finance are built; updated when endpoints or events change. Consumed by `backend-developer`, `qa-test-engineer`, `security-reviewer` and `devops-engineer`. Design context: `docs/architecture/ARCHITECTURE.md`, ADR-005 (maker-checker), ADR-006 (payments), ADR-007 (ledger), ADR-008 (auth).

## 0. Phase boundary
Phase 1 (built): Gateway, Identity, Claims (decision maker-checker), Reporting, Notification. **Phase 2 (designed, not built): Finance (Payment, Ledger, Pool Fund) and settlement maker-checker.** All Finance rows, threats S17-S31 and the Finance matrix below are Phase 2 requirements, kept so the design is reviewed now and implemented later; they gate Phase 2, not Phase 1. Phase 1 must already satisfy S1-S16 for the decision, and must store no bank details.

## 1. Assets and trust boundaries
- Assets: claim data (personal and financial); **bank account details of payees**; JWT signing key; officer decisions and amounts; the **ledger and pool fund balances**; payment instructions; the event streams `claims.events` and `finance.events`.
- Boundaries: Browser → Gateway (untrusted input) · Gateway → services (JWT revalidated by every service; role claims trusted only after validation) · Claims → Kafka → Finance, and Finance → Kafka → Claims (events are untrusted input, validated against local data) · Finance → bank (simulated; in production an external trust boundary) · Finance → Claims internal REST (service token, not routed by the Gateway) · services → their own database only; Finance modules → own schema only.
- Highest-value targets: bank details (disclosure), payment instruction (diversion, double pay), maker-checker (self-approval), ledger (tampering), fund (unauthorised top-up or drain).

## 2. Threat model (STRIDE, short)
| Threat | Example here | Mitigation | Verified by |
|---|---|---|---|
| Spoofing | Forged or expired JWT; forged event on Kafka claiming `SettlementConfirmed` | Validate signature, issuer, audience, expiry at Gateway and every service; Finance validates each event against local data (claim_ref payable and claimant, schema version) and DLQs mismatches; Kafka ACLs are a production gap | S1, S27 |
| Spoofing | Calling internal reconciliation endpoint as a user or via the Gateway | Internal routes not mapped in Gateway; require role `Service`; user tokens get 403 | S28 |
| Tampering | Client sets `status`, `assignedOfficerId`, `reserve`, `makerId`, `checkerId`, `amount` in a request; checker edits proposal | Request DTOs expose only client-settable fields; confirm carries only `proposalId`; actor identity from token only | S5, S16 |
| Tampering | Edit or delete a ledger entry or claim_history row; unbalanced entry | Ledger/trail DB role INSERT+SELECT only, trigger blocks UPDATE/DELETE; balanced-entry check in domain and DB; corrections are reversing entries | S22, S23 |
| Tampering | Replayed or duplicated payment/top-up request pays or posts twice | Unique idempotency key per attempt, unique `source_event_id`, `Idempotency-Key` on top-ups and payee submits, per-claim unique active payment | S20, S21, S25 |
| Tampering | Two payments both pass the funds check and overdraw the fund | Check + post in one transaction under a per-currency advisory lock; available = balance 1000 minus 2200 | S26 |
| Repudiation | Officer denies a proposal; manager denies a confirm, cancel or top-up | Trail entries with actor, role, makerId, checkerId, proposalId, reason; fund-level audit for top-ups; ledger entries link claim, payment, eventId; correlationId in logs | Unit + review |
| Information disclosure | Claimant reads another's claim or payee data (BOLA/IDOR); full account number in API, trail, events, logs or error; stack trace | Object-level checks; **bank details masked (last 4) on every read**, full number only stored encrypted in Finance; Serilog destructuring policy and event schema forbid account fields; problem details only | S2, S6, S17, S18 |
| Information disclosure | Claimant sees fund balance, Held reason, payment failures or internal notes | Claimant API has no fund or failure fields; Finance read endpoints Manager-only; Public/Internal trail split | S24, S13b |
| Denial of service | Huge payload, unbounded page size, poison Kafka message, retry storm against the bank, DLQ flood | Size limits, max page size, DLQ; retries capped at 4 attempts with backoff; outbox/consumer batch limits | S7, S8, S19b |
| Elevation of privilege | Claimant calls officer endpoint; officer settles unassigned claim; **officer confirms own proposal; dual-role user confirms own proposal**; officer retries/cancels payments or views ledger | Named policy on every endpoint, default deny; assignment rule; maker-not-checker in the domain on user id plus DB CHECK; Finance manager endpoints Manager-only | S3, S4, S11 to S15, S19, S24 |
| Elevation of privilege | Simulated bank controls or StubPayment active in a real environment | Only compiled/registered when `ASPNETCORE_ENVIRONMENT=Development`; startup test asserts absence otherwise | S29 |
| Fraud | Payment to a third party; diversion by changing payee after confirmation; manager top-up to inflate fund then self-benefit | Only the claim's claimant submits payee details; name match vs policyholder or justification (BR-43); bank details snapshot at confirmation, later correction is a new revision with reason and trail; top-up manager-only with reason and audit; **no maker-checker on top-up (gap)** | S17, S25 |

## 3. Authorisation matrix (default deny; architect to confirm)
Legend: allow / deny. "Own" = the claim belongs to the caller. Any endpoint not listed: **deny**. Unauthenticated: 401 on everything except `POST /auth/token`.

### Claims service
| Endpoint | Claimant | Officer | Manager | Extra rule |
|---|---|---|---|---|
| `POST /claims` | allow | deny | deny | Policy must belong to claimant |
| `GET /claims/mine` | allow | deny | deny | Own claims only |
| `GET /claims/{id}` | allow | allow | allow | Claimant: own claim only; claimant view has no fund, Held or failure data |
| `GET /claims/{id}/timeline` | allow (Public only) | allow (all) | allow (all) | Claimant: own claim only |
| `GET /claims` (queue) | deny | allow | allow | Paged, max page size |
| `POST /claims/{id}/assign` (pick-up) | deny | allow | deny | Race-safe; 409 if taken |
| `POST /claims/{id}/reassign` | deny | deny | allow | Auto-withdraws a pending proposal |
| `POST /claims/{id}/info-requests` | deny | allow | deny | Assigned officer only |
| `POST /claims/{id}/info-responses` | allow | deny | deny | Own claim, status InfoRequested |
| `POST /claims/{id}/assessment` | deny | allow | deny | Assigned officer; 409 if Pending* |
| **Propose** `POST /claims/{id}/decision-proposals` | deny | allow | deny | Assigned officer (maker); status UnderReview; reason on reject; the user is recorded as maker |
| **Confirm** `POST /claims/{id}/decision-proposals/{pid}/confirm` | deny | deny | allow | Caller `sub` != proposal `makerId`, even if the caller also holds Officer; `pid` must be the open proposal; no editable payload |
| **Send back** `POST .../{pid}/send-back` | deny | deny | allow | Not the maker; reason min 10 chars |
| **Withdraw** `POST .../{pid}/withdraw` | deny | allow | deny | Only the proposal's maker; reason required |
| **Request settlement** (Phase 2) `POST /claims/{id}/settlement-proposals` | deny | allow | deny | Assigned officer; status Approved; payee on file; name-match justification if needed |
| **Confirm / send back / withdraw settlement** (Phase 2) | deny | withdraw only (maker) | confirm and send back | Same maker-not-checker rule; confirm creates payment via event |
| `GET /internal/claims/reserves` (reconciliation) | deny | deny | deny | Role `Service` only; not routed by Gateway |

### Finance service (Payment, Ledger, Pool Fund) - PHASE 2: designed, not built
| Endpoint | Claimant | Officer | Manager | Extra rule |
|---|---|---|---|---|
| **Payee details submit** `PUT /claims/{id}/payee-details` | allow | deny | deny | Own claim only (checked against `claim_ref.claimantId`); claim must be Approved; `Idempotency-Key`; format validation only; response masked |
| `GET /claims/{id}/payee-details` | allow (own, masked) | deny | allow (masked) | Never returns full account number |
| `GET /payments` (queue: Failed, Held) | deny | deny | allow | Paged; masked bank data |
| `GET /payments/{id}` | deny | deny | allow | Masked |
| **Payment retry** `POST /payments/{id}/retry` | deny | deny | allow | Status Failed only; reason; new attempt number |
| **Payment correct payee** `POST /payments/{id}/payee-correction` | deny | deny | allow | Write-only (accepts full details, returns masked); new revision with reason; status Failed or Held |
| **Payment cancel** `POST /payments/{id}/cancel` | deny | deny | allow | Pending, Held or Failed only (409 on Processing or Paid); reason mandatory |
| Payment release Held `POST /payments/{id}/release` | deny | deny | allow | Still subject to funds check |
| `GET /funds`, `GET /funds/{market}` | deny | deny | allow | Fund balance, available, coverage ratio |
| **Fund top-up** `POST /funds/{market}/top-ups` | deny | deny | allow | Positive amount with sane maximum, reason required, `Idempotency-Key` (request id) |
| **Ledger view** `GET /ledger/entries`, `/ledger/accounts/{code}/balance` | deny | deny | allow | Read-only, paged |
| **Trial balance** `GET /ledger/trial-balance` | deny | deny | allow | Read-only |
| **Reconciliation** `GET /ledger/reconciliation`, `POST /ledger/reconciliation/run` | deny | deny | allow | Report only; no correcting action |
| Any POST/PUT/DELETE on ledger entries | deny | deny | deny | Not exposed: ledger is fed by events only |
| Simulated bank control `POST /dev/sim-bank` | deny | deny | deny | Exists only in Development; absent otherwise |

### Reporting and Notification
| Endpoint | Claimant | Officer | Manager | Extra rule |
|---|---|---|---|---|
| `GET /dashboard/*`, `GET /staff/workload`, checker queue, payment stats | deny | deny | allow | Read model only |
| `GET /notifications/mine` | allow | allow | allow | Own notifications only; manager alerts only to Manager |

Principles: (1) **default deny** with a fallback policy that requires authentication; a startup test fails if an endpoint has no named policy. (2) **Maker-not-checker** is enforced in the domain on user id and backed by a DB CHECK; the controller role gate is only a coarse first filter. (3) Identity comes from the token, never the body. (4) Every refused action by an authenticated user is logged and written as an Internal trail entry (claims) or log entry (Finance). (5) Finance never exposes a bank account number: reads are masked, corrections are write-only.

## 4. Mandatory automated checks (gate)
Run `scripts/security-check.sh` (owned by `devops-engineer`). A change is not ready until it exits 0.
- Secret scan of working tree and history (gitleaks). Dev signing keys and the bank-detail encryption key must be dev-only values, clearly named.
- Dependency vulnerabilities: `dotnet list package --vulnerable --include-transitive` (backend), `npm audit --audit-level=high` (frontend).
- Container image scan of built images (trivy) when images exist.
- Format and analyser warnings as errors.
- Architecture tests: Finance modules do not reference each other's schemas or DbContexts; no service references another service's domain.
- Log-leak check: integration tests capture logs and assert no account number, token or full payee payload.
Results are attached to the status report by the `orchestrator`; a skipped check must be stated and justified.

## 5. Security test list (owned by `qa-test-engineer`)
S1-S16 apply to Phase 1 (S11-S16 on the decision). **S17-S31 are Phase 2** (Finance and settlement).

| ID | Test | Expected |
|---|---|---|
| S1 | No token, malformed token, expired token, wrong audience | 401 |
| S2 | Claimant A requests claimant B's claim | 403/404, no data leaked |
| S3 | Claimant calls each officer endpoint; officer calls claimant-only endpoints; non-manager calls dashboards | 403 |
| S4 | Officer B tries to decide or settle a claim assigned to officer A | 403/409 |
| S5 | Request body includes `status`, `assignedOfficerId`, `reserve`, `approvedAmount`, `makerId` | Ignored or 400; server value unchanged |
| S6 | Trigger validation error, not-found and server error | RFC 7807 body, no stack trace, no internals, no PII in logs |
| S7 | Oversized body, over-max page size, negative or absurd amount | 400/413, bounded |
| S8 | Malformed, wrong-version and duplicate Kafka events | DLQ or skipped; consumer keeps running; no double apply |
| S9 | Invalid state transitions (approve a Submitted claim, settle a Rejected claim, confirm a non-pending claim) | 409/422 |
| S10 | Concurrent pick-up by two officers | Exactly one 200, one 409 |
| S11 | User with both Officer and Manager roles proposes, then confirms the same proposal | 403 `maker-cannot-check`, refused Internal entry, state unchanged |
| S12 | Officer-only user calls confirm/send-back | 403 |
| S13 | Non-assigned officer proposes or withdraws; non-maker withdraws | 403 |
| S13b | Claimant timeline during a pending proposal and after a send-back | No outcome, amount, reason, names, send-back |
| S14 | Confirm with stale `proposalId` (maker withdrew and re-proposed) | 409, nothing applied |
| S15 | Two simultaneous confirms (and confirm racing withdraw) | One success, others 409 plus refused entries; one transition; one payment later |
| S16 | Confirm request carries an amount or outcome | Ignored; applied exactly as proposed |
| S17 | Claimant B submits payee details for claimant A's claim; payee submit on non-Approved claim; officer submits | 403/404; 409; 403 |
| S18 | Submit payee details, then read API, trail, events (consumer capture), logs | Only `accountLast4`; no full number anywhere |
| S19 | Officer or claimant calls payment retry, correct, cancel, release | 403 |
| S19b | Gateway set to fail always | Exactly 4 attempts then Failed; no further automatic attempts |
| S20 | Same idempotency key sent twice; timeout then success | One payout, one set of ledger entries |
| S21 | Replay the same Kafka event ten times (SettlementConfirmed, PaymentCompleted) | One payment, one journal entry, one Settled |
| S22 | Post unbalanced or mixed-currency journal entry | 422/refused whole, nothing persisted |
| S23 | UPDATE or DELETE on journal tables and claim_history as the app role | Fails at DB level |
| S24 | Officer or claimant requests ledger, trial balance, funds, top-up; claimant payloads inspected | 403; no fund or Held fields in claimant responses |
| S25 | Top-up with negative, zero, huge amount, missing reason; repeated request id | 400/422; one entry for repeated id |
| S26 | Parallel payments exceeding available funds | Only affordable ones dispatched; rest Held; fund never negative |
| S27 | `SettlementConfirmed` with amount different from approved payable, or unknown claim | DLQ, no payment, alert |
| S28 | Call internal reserve endpoint via Gateway and with user token | 404 at Gateway; 403 with user token |
| S29 | Start services with a non-Development environment | Simulated bank controls and StubPayment absent |
| S30 | `PaymentCompleted` for a claim not in Settling | DLQ, claim unchanged |
| S31 | Manager cancels Processing or Paid payment | 409 |

## 6. Production gaps (stated honestly in the walkthrough)
Mock JWT (RS256 dev keys, no revocation) instead of OIDC; no TLS in local compose; no rate limiting or WAF; no secrets manager or KMS (bank details encrypted with a dev key); no Kafka ACLs/encryption, so event authenticity relies on payload validation; no audit log retention policy; **no maker-checker on fund top-ups** (manager-only, audited); no hash-chaining or external anchoring of the ledger; bank gateway is simulated, so no mutual TLS, request signing or bank-side reconciliation file; no PCI-style data classification process; DLQ replay is manual and not built; no segregation of Finance DB credentials per module beyond schema grants.
