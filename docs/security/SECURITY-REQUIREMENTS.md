# Security Requirements – Chubb Claims Platform

Status: **DRAFT**. Owner: `architect`. Written before Claims and Finance are built; updated when endpoints or events change. Consumed by `backend-developer`, `qa-test-engineer`, `security-reviewer` and `devops-engineer`. Design context: `docs/architecture/ARCHITECTURE.md`, ADR-005 (maker-checker), ADR-006 (payments), ADR-007 (ledger), ADR-008 (auth), ADR-009 (idempotent commands). Last updated 2026-10-28 for the Claims slice (idempotency, endpoint list, payee-details Gateway guard) and the claims-domain spec review (System role, claimant status masking, event payload hygiene; ADR-010), and the claims-persistence review (idempotency executor split; per-claim coverage cap gap, decision log 16).

## 0. Phase boundary
Phase 1 (built): Gateway, Identity, Claims (decision maker-checker), Reporting, Notification. **Phase 2 (designed, not built): Finance (Payment, Ledger, Pool Fund) and settlement maker-checker.** All Finance rows, threats S17-S31 and the Finance matrix below are Phase 2 requirements, kept so the design is reviewed now and implemented later; they gate Phase 2, not Phase 1. Phase 1 must already satisfy S1-S16 for the decision, and must store no bank details.

## 1. Assets and trust boundaries
- Assets: claim data (personal and financial); **bank account details of payees**; JWT signing key; officer decisions and amounts; the **ledger and pool fund balances**; payment instructions; the event streams `claims.events` and `finance.events`.
- Boundaries: Browser → Gateway (untrusted input) · Gateway → services (JWT revalidated by every service; role claims trusted only after validation) · Claims → Kafka → Finance, and Finance → Kafka → Claims (events are untrusted input, validated against local data) · Finance → bank (simulated; in production an external trust boundary) · Finance → Claims internal REST (service token, not routed by the Gateway) · services → their own database only; Finance modules → own schema only. **Network (local compose):** only the Gateway publishes a port (bound to `127.0.0.1`); the Gateway and services share the `edge` network; Postgres (and Kafka when added) sit on the `internal` `data` network, which the Gateway is not on and which has no route out; Postgres publishes no port.
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
| Elevation of privilege | Claimant calls officer endpoint; officer settles unassigned claim; **officer confirms own proposal; dual-role user confirms own proposal**; officer retries/cancels payments or views ledger | Named policy on every endpoint, default deny; assignment rule; maker-not-checker in the domain on user id plus DB CHECK; the domain repeats the role check as defence in depth (untrailed 403 `forbidden`; trail `actorRole` always taken from the actor's real roles, ADR-005 amendment); Finance manager endpoints Manager-only | S3, S4, S11 to S15, S19, S24 |
| Elevation of privilege | Simulated bank controls or StubPayment active in a real environment | Only compiled/registered when `ASPNETCORE_ENVIRONMENT=Development`; startup test asserts absence otherwise | S29 |
| Spoofing (Identity/Gateway) | `alg=none` token, HS256 token signed with the public key as secret, token from another issuer/key | `ValidAlgorithms = [RS256]`, `RequireSignedTokens`, issuer `chubb-identity`, audience `chubb-claims-api`, public key from file only, at Gateway and every service (ADR-008 amendment) | S1 (all variants) |
| Spoofing (Identity/Gateway) | Client sends `X-User-Id`/`X-User-Role` or body `actorId`/`role`/`market` to impersonate | Gateway forwards only an **allowlist** of request headers (`Authorization`, `Accept*`, cache/conditional headers, `Range`, `User-Agent`, trace headers, `Idempotency-Key`) and sets `X-Correlation-Id` itself; every other header (`X-User-*`, `X-Forwarded-User`, `Remote-User`, cookies) is dropped and client `X-Forwarded-*` values are not passed through. Services still read identity only from the validated token (never a header, even one set by the Gateway); login DTO has only `email`, `password`. A new header must be added to the allowlist deliberately | S5, spec AC-6 (`Gateway_SpoofedIdentityHeaders_AreNotForwarded`) |
| Spoofing (Identity) | Credential stuffing or password guessing on `POST /auth/token` | Uniform 401 `invalid-credentials`; PBKDF2 hashes; bounded body (4 KB); **no rate limiting or lockout (production gap)** | S7, review |
| Information disclosure (Identity) | User enumeration via different errors or timing; email/password/token in logs | Same body and status for unknown email and wrong password (verify against a dummy hash when user unknown); request logging excludes `Authorization` and `/auth/token` bodies; failure logs carry no email | spec AC-2, log-leak test |
| Information disclosure (Gateway) | Merged OpenAPI or Swagger UI exposes internal routes; upstream errors leak internals | Merge drops `/internal/**`, `/health/**`; served only in Development; Gateway errors are problem details without upstream detail | S6, S28 |
| Information disclosure | Dev signing private key committed or readable by every container | Generated at first `compose up` by an init container, never committed; private volume mounted only into Identity, public volume into the rest | secret scan, compose review |
| Information disclosure / Elevation of privilege (runtime) | One compromised container reads every secret or connects to another service's database; Gateway (internet-facing) reaches Postgres directly | **Per-service secrets:** each compose service lists only the environment keys it needs (no shared `env_file`); the Gateway holds no DB or Identity secret. **Per-service DB role** with its own password, owner of only its database, `REVOKE ALL ... FROM PUBLIC`. **Network split:** Postgres on the internal `data` network with no published port; the Gateway is only on `edge`. App containers read-only, `cap_drop: ALL`, `no-new-privileges`; only the Gateway port is published, on loopback | compose review, security-reviewer |
| Elevation of privilege (Gateway) | Open-proxy behaviour: anonymous catch-all route forwards arbitrary paths; `/internal/**` or `/dev/**` reachable; CORS wildcard lets any site call the API | Only `POST /auth/token` is routed anonymously (exact path and method); `/internal/**`, `/dev/**` unmapped (404); one CORS policy with configured origins, no wildcard, no credentials | S28, spec AC-7 |
| Information disclosure (Angular client) | Injected script reads the JWT from `sessionStorage`; the interceptor sends the token to a look-alike or third-party origin; raw problem `detail` or the token appears in UI or logs | Token in memory mirrored to `sessionStorage` only (never `localStorage`, URL or logs); Bearer attached only when `new URL(req.url).origin` equals the configured Gateway origin; no `innerHTML`/`bypassSecurityTrust*`; problem details mapped to fixed messages; decoded claims are display/routing only (guards are UX). **httpOnly-cookie/BFF and CSP are production gaps** | frontend foundation SPEC AC-6, AC-7, AC-13 tests |
| Tampering / Repudiation (Claims idempotency, ADR-009) | A resent command (weak network, double-click, scripted replay) creates a second claim, a second transition or a second Refused entry, inflating refused-attempt counts or hiding a real attempt among duplicates | `Idempotency-Key` mandatory on every Claims command (startup test: no non-GET endpoint without the filter); key record commits in the same transaction as state, trail and outbox; same-scope concurrency serialised by a transaction-scoped advisory lock (duplicate gets 409, handler never runs); replay writes nothing; different payload under the same key gets 422 | I1-I8, S32, S35 |
| Information disclosure (Claims idempotency) | Attacker replays or guesses another user's key to read the stored response (claim details, an answer to an info request); stored bodies leak into logs | Scope is `(actor_id from the validated token, method + canonical route, key)`: a key from another actor is a separate record and executes as that actor's own request, so it never receives the stored response. Authentication and the role policy run before any lookup. A 422 `idempotency-key-reused` never echoes the stored body. Stored bodies are never logged (no request/response body logging in Claims, EF `EnableSensitiveDataLogging` off, the store logs only route template, outcome (executed, replayed, reused, in-progress) and status code; never the key, the request hash or the body). Ignored on lookup after 24 h and deleted by the expiry path. No read endpoint exposes the table | S33, S34, log-leak test |
| Denial of service (Claims idempotency) | Flood of unique keys fills `idempotency_keys`; huge bodies stored; lock contention | Only stored for requests that passed authentication and the role policy and committed something; Claims body limit 64 KB; 24 h expiry indexed; advisory lock is try-only (no waiting connections) | S7, review; **no per-user rate limit (gap)** |
| Elevation of privilege (Claims domain, ADR-010) | A token carrying role `System` (forged, or a seed mistake) is accepted as a privileged actor; a domain method takes an actor id from a payload | `Roles.System` is a trail `actorRole` only. Identity never issues it (no seeded System user), no Claims/Reporting/Notification policy lists it, and the Application builds `Actor.System` in code only for machine actions (none in Phase 1). Domain methods take the actor only from `CommandContext`, which is built from the validated token (BR-11) | S36, spec 1 section 8 |
| Information disclosure (claimant projections, BR-33) | Claimant infers a send-back or a pending proposal from a status flip, a new Public entry, entry count, or a staff-only field (reserveBefore/After, makerId, proposalId) on a Public entry | Status masked in the domain projection (PendingDecisionApproval shown as UnderReview); the Public companion is written only on the first proposal; the claimant timeline item exposes only `id, occurredAt, summary`; the claimant DTO is a separate type | S13b, S37 |
| Information disclosure (claims events) | Free text (info answer, rationale, reasons, corrected statement) or personal data reaches Kafka and every consumer's database | Payload records are primitive-only with no free-text fields (ADR-010), golden-file tests per record, and a unit test that every `PendingEvents` payload has no string field outside an allow-list (codes, ids, currency, claim number, status) | spec 1 `PendingEvents_EachCommand_RaisesExpectedPayloadWithoutFreeText`, review |
| Fraud | Payment to a third party; diversion by changing payee after confirmation; manager top-up to inflate fund then self-benefit | Only the claim's claimant submits payee details; name match vs policyholder or justification (BR-43); bank details snapshot at confirmation, later correction is a new revision with reason and trail; top-up manager-only with reason and audit; **no maker-checker on top-up (gap)** | S17, S25 |

## 3. Authorisation matrix (default deny; architect to confirm)
Legend: allow / deny. "Own" = the claim belongs to the caller. Any endpoint not listed: **deny**. Unauthenticated: 401 on everything except `POST /auth/token`, health endpoints and (Development only) the Gateway's `/openapi/v1.json` and `/swagger`.

### Claims service
Full contract (paths, policies, problem types, idempotency) is in ARCHITECTURE section 9, "Claims API contract". **Every non-GET row below requires `Idempotency-Key` (ADR-009, BR-79 to BR-87).** "Refused" = Internal Refused entry written and the refusal stored for replay. Role-policy denials (the "deny" cells) are logged and, under the rule **DECIDED (human, 2026-10-28)** in ADR-009, not trailed and not stored. Claimant ownership failures return 404 and are not trailed.

| Endpoint | Claimant | Officer | Manager | Extra rule |
|---|---|---|---|---|
| `POST /claims` | allow | deny | deny | Policy must belong to claimant (else 422 `policy-not-found`, same as unknown policy) |
| `GET /claims/mine` | allow | deny | deny | Own claims only |
| `GET /claims/{id}` | allow | allow | allow | Claimant: own claim only; claimant view has no fund, Held or failure data |
| `GET /claims/{id}/timeline` | allow (Public only) | allow (all) | allow (all) | Claimant: own claim only |
| `GET /claims` (queue) | deny | allow | allow | Paged, max page size |
| `POST /claims/{id}/assign` (pick-up) | deny | allow | deny | Race-safe; 409 if taken |
| `GET /claims/reference-data` | allow | allow | allow | Static reference data only |
| `POST /claims/{id}/reassign` | deny | allow (current assignee only: release to a named officer) | allow | Reason required; target must be a known officer other than the current one; auto-withdraws a pending proposal; non-assignee officer gets 403 Refused. Officer release = hand to a named officer, never back to Submitted (**DECIDED (human, 2026-10-28)**, ADR-009) |
| `POST /claims/{id}/info-requests` | deny | allow | deny | Assigned officer only |
| `POST /claims/{id}/info-responses` | allow | deny | deny | Own claim (else 404), status InfoRequested; the answer is personal data and is also held in the idempotency record for 24 h |
| `POST /claims/{id}/assessed-amount` | deny | allow | deny | Assigned officer; UnderReview; 409 if Pending* (renamed from `/assessment` to match BR-79 scope and TASKS 4.6) |
| `POST /claims/{id}/corrections` | deny | allow (assigned) | allow | BR-74; Internal only; non-assigned officer gets 403 Refused |
| **Propose** `POST /claims/{id}/decision-proposals` | deny | allow | deny | Assigned officer (maker); status UnderReview; reason on reject; the user is recorded as maker |
| **Confirm** `POST /claims/{id}/decision-proposals/{pid}/confirm` | deny | deny | allow | Caller `sub` != proposal `makerId`, even if the caller also holds Officer; `pid` must be the open proposal; no editable payload |
| **Send back** `POST .../{pid}/send-back` | deny | deny | allow | Not the maker; reason min 10 chars |
| **Withdraw** `POST .../{pid}/withdraw` | deny | allow | deny | Only the proposal's maker; reason required |
| **Request settlement** (Phase 2) `POST /claims/{id}/settlement-proposals` | deny | allow | deny | Assigned officer; status Approved; payee on file; name-match justification if needed |
| **Confirm / send back / withdraw settlement** (Phase 2) | deny | withdraw only (maker) | confirm and send back | Same maker-not-checker rule; confirm creates payment via event. In Phase 1 all settlement routes return 409 `phase-2-not-enabled` (Refused) |
| `/claims/{id}/payee-details` (any method) | - | - | - | Never reaches Claims in Phase 1: the Gateway guard answers 501 (authenticated) / 401 (anonymous) |
| `GET /internal/claims/reserves` (reconciliation) | deny | deny | deny | Role `Service` only; not routed by Gateway |

### Finance service (Payment, Ledger, Pool Fund) - PHASE 2: designed, not built
Gateway routing (ARCHITECTURE section 2a): `/payments/{**}`, `/funds/{**}`, `/ledger/{**}` and the exact `/claims/{id}/payee-details` route (ordered before the `/claims/{**}` prefix route so bank details never reach Claims) go to Finance with the `authenticated` Gateway policy. They are **not** in the Phase 1 Gateway config (no commented-out block), so in Phase 1 `/payments`, `/funds` and `/ledger` return 404. The payee-details path is instead answered by a code-mapped Gateway endpoint (`src/Gateway/Api/PayeeDetailsRoute.cs`): 501 for authenticated callers, 401 for anonymous ones, never forwarded, because otherwise it would match the `/claims/{**}` route and bank details would reach Claims. Adding the Finance routes is part of the Finance slice: remove the guard, add the YARP route, and replace the 501/401 tests with a Gateway test asserting payee-details routes to Finance and never to Claims.
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

### Identity and Gateway (Phase 1, confirmed 2026-10-28 with the identity-gateway spec)
| Endpoint | Anonymous | Claimant | Officer | Manager | Extra rule |
|---|---|---|---|---|---|
| `POST /auth/token` (Identity, routed by exact path and method) | allow | allow | allow | allow | Body `email`, `password` only; 4 KB limit; uniform 401; the only anonymous business route |
| `GET /health/live`, `GET /health/ready` (each service, own port) | allow | allow | allow | allow | Not routed through the Gateway for downstream services; no detail beyond status |
| `GET /openapi/v1.json`, `/swagger` (Gateway) | allow | allow | allow | allow | **Development only** (absent otherwise, startup test); excludes `/internal/**`, `/health/**` |
| Any other `/auth/**` path | 404 | 404 | 404 | 404 | Not routed |
| `/internal/**`, `/dev/**` via Gateway | 404 | 404 | 404 | 404 | Unmapped (S28) |
| Any routed path without a valid token | 401 | - | - | - | Gateway fallback policy, then revalidated by the service |

The Gateway enforces authentication only; the role and ownership columns for `/claims`, `/dashboard`, `/staff`, `/notifications` are enforced in the owning service (rows below). Minimal hosts created before their slice (Claims, Reporting, Notification) must already wire JWT validation and the fallback policy, so they are default-deny from the first commit; each carries its own copy of that setup (no shared code beyond `Contracts`), so each service's slice must include its own S1 variants (no token, wrong key, `alg=none`, HS256-with-public-key) to stop the copies drifting. Identity validates no tokens: it registers a fallback authenticated policy with no authentication scheme, so any endpoint added without an explicit policy fails closed (server error, never served), plus an anonymous catch-all that returns 404 for unmapped paths; AC-10's startup test remains the primary guard. The Gateway also answers unmapped paths with an anonymous 404 (route existence is not secret: it is in the OpenAPI document).

### Reporting and Notification (paths fixed 2026-10-28, identity-gateway Q7)
| Endpoint | Claimant | Officer | Manager | Extra rule |
|---|---|---|---|---|
| `GET /dashboard/outstanding` | deny | deny | allow | Read model only (US-13) |
| `GET /dashboard/exposure` | deny | deny | allow | Read model only (US-14) |
| `GET /dashboard/pending-approvals` (checker queue) | deny | deny | allow | Read model only; Officer 403 (US-30) |
| `GET /staff/workload` | deny | deny | allow | Read model only (US-10) |
| `GET /dashboard/stream` (SSE, COULD) | deny | deny | allow | Only if built; must stay under `/dashboard` so no new Gateway route |
| Any other `/dashboard/*` (COULD: performance, payment stats P2) | deny | deny | allow | Added to this table by the Reporting spec before it is built |
| `GET /notifications/mine` | allow | allow | allow | Own notifications only (recipient = token `sub`); manager alerts only to Manager; claimant content Public-only |

Principles: (1) **default deny** with a fallback policy that requires authentication; a startup test fails if an endpoint has no named policy. (2) **Maker-not-checker** is enforced in the domain on user id and backed by a DB CHECK; the controller role gate is only a coarse first filter. (3) Identity comes from the token, never the body. (4) Every refused action by an authenticated user is logged; in Claims, refusals by the domain or resource rules (invalid transition, non-assigned officer, maker equals checker, non-maker withdraw, lost race) are also written as an Internal trail entry, atomically with the stored idempotency response. Role-policy 403s and claimant-ownership 404s are logged only, never trailed or stored: **DECIDED (human, 2026-10-28)** (ADR-009), which also stops any authenticated user writing entries into other claims' trails. Finance writes log entries. (5) Finance never exposes a bank account number: reads are masked, corrections are write-only.

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
S1-S16 and S32-S35 apply to Phase 1 (S11-S16 on the decision; S32-S35 on Claims idempotency, alongside functional tests I1-I8 in TASKS section 8). **S17-S31 are Phase 2** (Finance and settlement).

| ID | Test | Expected |
|---|---|---|
| S1 | No token, malformed token, expired token, wrong audience | 401 |
| S2 | Claimant A requests claimant B's claim | 403/404, no data leaked |
| S3 | Claimant calls each officer endpoint; officer calls claimant-only endpoints; non-manager calls dashboards | 403; for Claims commands (ADR-009, DECIDED (human, 2026-10-28)) entry count unchanged, no idempotency record, one security log event |
| S4 | Officer B tries to decide or settle a claim assigned to officer A | 403/409 |
| S5 | Request body includes `status`, `assignedOfficerId`, `reserve`, `approvedAmount`, `makerId` | Ignored or 400; server value unchanged |
| S6 | Trigger validation error, not-found and server error | RFC 7807 body, no stack trace, no internals, no PII in logs |
| S7 | Oversized body, over-max page size, negative or absurd amount | 400/413, bounded |
| S8 | Malformed, wrong-version and duplicate Kafka events | DLQ or skipped; consumer keeps running; no double apply |
| S9 | Invalid state transitions (approve a Submitted claim, settle a Rejected claim, confirm a non-pending claim) | 409/422 |
| S10 | Concurrent pick-up by two officers | Exactly one 200, one 409 |
| S11 | User with both Officer and Manager roles proposes, then confirms the same proposal | 403 `maker-cannot-check`, refused Internal entry, state unchanged |
| S12 | Officer-only user calls confirm/send-back | 403 (ADR-009, DECIDED (human, 2026-10-28)): trail entry count unchanged, no idempotency record, one security log event; a resend with the same key executes again (not a replay) |
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
| S32 | Claims idempotency coverage: startup/reflection test over the endpoint data source; plus one non-GET command sent without a key | Every non-GET Claims endpoint has idempotency metadata and a named policy; keyless request gets 400 `idempotency-key-missing`, entry count unchanged |
| S33 | Cross-actor replay: officer A commits with key k1; officer B (and the dual-role user) sends k1 with identical bytes on the same route; claimant B sends claimant A's key on A's info-response route | B executes as B (own result or own refusal) and never receives A's body; claimant B gets 404; A's record unchanged (BR-80) |
| S34 | Stored response hygiene: run an info-response with a recognisable answer string and a submit; capture all logs; inspect the 422 `idempotency-key-reused` body; set `expires_at` in the past | Answer text and the key appear in no log line; the 422 body contains no stored content; the expired record is not replayed and the request executes as new (BR-81, BR-83, BR-87) |
| S35 | Replay after expired or changed token: replay a stored command with an expired token, and with no token | 401 before any lookup; no `Idempotent-Replayed` header (BR-82) |
| S36 | Token with role `System` only (signed with the dev key) calls each Claims command and a Reporting read | 403 on every endpoint (no policy accepts System); no trail entry |
| S37 | Claimant reads claim detail, list and timeline during a pending first proposal, after a send-back, after a re-proposal and after a withdrawal | Status reads UnderReview throughout; exactly one "Decision under review" entry; no reserve, proposal, maker or checker field in any claimant response |

## 6. Production gaps (stated honestly in the walkthrough)
Mock JWT (RS256 dev keys, no revocation, single shared audience, no JWKS rotation) instead of OIDC; no login rate limiting or account lockout; browser holds the bearer token in memory plus `sessionStorage` (readable by any injected script; production: BFF with an httpOnly SameSite cookie, or OIDC code flow with PKCE and memory-only tokens) and no Content-Security-Policy is served for the SPA; no TLS in local compose; no rate limiting or WAF; no secrets manager or KMS (bank details encrypted with a dev key); no Kafka ACLs/encryption, so event authenticity relies on payload validation; no audit log retention policy; **no maker-checker on fund top-ups** (manager-only, audited); no hash-chaining or external anchoring of the ledger; bank gateway is simulated, so no mutual TLS, request signing or bank-side reconciliation file; no PCI-style data classification process; DLQ replay is manual and not built; no segregation of Finance DB credentials per module beyond schema grants; compose secrets are plain environment variables (visible via `docker inspect`), with dev-only defaults inline in `docker-compose.yml`, and the `postgres` container holds the admin and every service role password for the init script (production: secrets manager, per-service injection, no inline defaults); the `edge` network is flat, so services can reach each other's HTTP ports directly (mitigated only by every service revalidating the JWT; production: network policies or mTLS); **Claims idempotency records** hold response bodies (possibly personal data) unencrypted for up to 24 h and there is no per-user limit on key creation (production: column encryption or storing only a minimal response, plus rate limiting); **the Claims DB role also runs migrations and owns `claim_history`**, so its INSERT/SELECT-only grant and the update/delete trigger stop application bugs but not a compromised Claims container (production: a separate migration role owns the schema, the runtime role has DML only); **Phase 1 caps each claim's payable at the policy coverage limit per claim only** (BR-19, decision log 16), so two approved claims on one policy can together exceed the limit. This is a financial-control gap, closed in Phase 2 by the aggregate remaining limit, re-checked at confirm under a policy-row lock (409 `coverage-limit-exceeded`); **staff are not scoped by market** (every Officer and Manager reads all six markets; the token `market` claim is display-only; production: market or team scoping in the claim queries); **Kafka message headers** (`event-id`, `event-type`, `event-version`) are unauthenticated copies of envelope fields, so consumers must trust only the validated envelope.
