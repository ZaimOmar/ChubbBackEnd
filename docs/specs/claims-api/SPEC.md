# SPEC: Claims API (endpoints, policies, DTOs, problem details, contract)  (service: Claims)

Spec 3 of the Claims specs (`docs/specs/README.md`). Wires spec 1 (domain) and spec 2 (persistence, **all command handlers**, `IOfficerDirectory`, HTTP-free `IdempotentCommandExecutor`) to HTTP. Code goes under `src/Claims/Api/`, the read side under `src/Claims/Application/Queries/` and `src/Claims/Infrastructure/Queries/`; the contract is `contracts/openapi/claims.yaml`. No business rule lives here: an endpoint binds, validates the shape, builds the context, calls one spec 2 handler and maps the result. This spec writes **no command handler and no `IOfficerDirectory`**.

| Field | Value |
|---|---|
| Author | backend-developer |
| Status | REVIEWED (architect final check 2026-10-28; see section 13) |
| Phase | P1 build (settlement stubs answer `phase-2-not-enabled`; `/internal/**` is Phase 2 and not mapped) |
| Stories (US-xx) | US-01 (submit), US-02, US-03 (claimant reads own claim and timeline), US-04/US-07 (info request and response), US-06 (pick-up), US-09 (manager reads any claim and the full trail), US-12 (reassign), US-18 (corrections), US-20 (actor and visibility from the token), US-22 (refused attempts), US-25 (OpenAPI client source, run locally), US-26 to US-29, US-31, US-30 (list filters), US-13/14 (staff lists) |
| Business rules (BR-xx) | BR-02, BR-04 (refused vs role-policy 403), BR-08/BR-09/BR-12/BR-33 (visibility, masking), BR-11 (identity from token), BR-13 to BR-16, BR-17 to BR-21, BR-26, BR-27, BR-32, BR-34, BR-36 to BR-39, BR-74, BR-76, BR-78, BR-79 to BR-87 (key handling at the edge) |
| Tasks (docs/planning/TASKS.md) | 2 (security gate), 4.1 (FluentValidation, problem details, pagination), 4.2 (timeline, masking, security log events), 4.3, 4.4 (endpoints), 4.6 (filter on every non-GET route), 8 (S1 to S16), 9 (compose, README client regeneration) |
| Related ADRs | ADR-003 (contract first), ADR-005, ADR-008 (policies, token), ADR-009 (idempotency, role-policy 403 decision), ADR-010 |

## 1. Purpose and scope
Expose the Claims commands and queries behind the Gateway with default-deny authorisation, a closed set of request DTOs, one problem-details shape, and a committed OpenAPI file the Angular client is generated from. Every non-GET endpoint carries `.WithIdempotency()` (spec 2) and a named policy; a startup test proves it.

**In scope:** 11 command routes and the 4 settlement stubs (15 non-GET routes), 6 GET routes, policies and the role-policy result handler, `ICommandContextFactory`/`ActorFactory`, request DTOs and FluentValidation validators, response views (claimant vs staff), read-side queries (detail, lists, timeline), problem-details mapping (`ProblemResults`, exception handler), the HTTP idempotency adapter (`IdempotencyFilter`, `.WithIdempotency()` metadata, `RequestBufferingMiddleware`, key-format validator) and its HTTP tests (one replay smoke test per route family), correlation id middleware, Serilog setup, health, `claims.yaml` and its drift test, startup coverage test (S32), the security test matrix for Claims, compose handoff.

**Out of scope / not now:** domain rules (spec 1); transaction, filter, store, recorder, seeder, migrations (spec 2); outbox publisher and Kafka (messaging-engineer); Gateway code, routes, CORS and the OpenAPI aggregator (owned separately, section 9); the frontend client (spec 4); `GET /internal/claims/reserves` and the `Service` policy (Phase 2, Q5); payee-details (answered by the Gateway guard, never reaches Claims); per-user rate limiting (documented gap); ETag/`If-Match` on commands (the concurrency token is internal, conflicts return 409).

## 2. Acceptance criteria
API-level Given/When/Then; each maps to tests in section 10.
- **AC-1 (US-01, BR-23/25/75)** Claimant posts a valid claim (by policy **number**) with `Idempotency-Key`: 201, `Location: /claims/{id}`, body `ClaimsClaimantClaimView` with `id` and `claimNumber`; currency comes from the policy and is not in the body; the same key and bytes again return the identical 201 with `Idempotent-Replayed: true`.
- **AC-2 (US-01, S7)** Invalid input returns 400 `validation-error` (missing fields, unknown field, estimate not positive or more than 2 decimals or above the cap, description too long, type/details mismatch) or 422 (`policy-not-found` also for an unknown policy number or another person's policy, `market-mismatch`, `incident-outside-policy-period`, `incident-date-in-future`); none is stored, and the same key may be reused with a corrected body.
- **AC-3 (US-06, BR-26)** Officer posts `/assign` with `{}`: 200 staff summary, status UnderReview, assignee = caller; second officer: 409 `invalid-transition` or `claim-already-assigned`, both Refused and stored.
- **AC-4 (US-12, BR-27, BR-37)** `/reassign` `{newOfficerId, reason}`: Manager any open non-Submitted claim, Officer only as assignee; 422 `unknown-officer` for an unknown id, 422 `same-officer` for the current assignee; 400 for a reason under 10 characters; 403 `not-assigned-officer` and 409 `invalid-transition` are Refused.
- **AC-5 (US-26)** `/assessed-amount` returns the updated reserve and net payable for the assigned officer; 409 `proposal-pending` while a proposal is open.
- **AC-6 (US-04, US-07, BR-13 to BR-16)** Officer requests information (200, due +14 days); claimant answers on own claim (200, back to UnderReview); another claimant gets 404; a second open request gets 409 `open-info-request-exists`.
- **AC-7 (US-27, BR-17 to BR-21)** `/decision-proposals` accepts only `outcome, reasonCode, rationale, justification`; the response is the staff view whose `openProposal.id` is the new proposal; amount is never accepted; 422 codes are not stored.
- **AC-8 (US-28, US-29, US-31, BR-32 to BR-39, S16)** `/confirm` takes no payload and applies the stored proposal; `/send-back` and `/withdraw` need `reason` of at least 10 characters; maker-cannot-check, not-proposal-maker, stale pid and lost race are Refused 403/409 and stored.
- **AC-9 (US-18, BR-74)** `/corrections` for the assigned officer or any Manager; 422 `entry-not-found` for an entry that is not on this claim (checked after the resource rule, never before it); status, reserve and events unchanged.
- **AC-10 (BR-01, TASKS 4.1)** The four settlement stub routes return 409 `phase-2-not-enabled` in every status, Refused and stored; the route policy still applies (Officer for propose/withdraw, Manager for confirm/send-back).
- **AC-11 (US-02, US-03, US-20, BR-12, BR-33)** A claimant reads only own claims (`/claims/mine`, detail, timeline); another claimant's or a missing id returns the same 404 `claim-not-found`; the claimant view never contains reserve, assessed amount, excess, net payable, assignee, proposal, maker or checker; `PendingDecisionApproval` reads as `UnderReview`; the timeline holds Public entries only with `id, occurredAt, summary`.
- **AC-12 (US-09, BR-12)** Officer and Manager read any claim and the full timeline; Internal entries are marked `visibility: Internal`; entries that a Correction references carry `correctedByEntryId`.
- **AC-13 (US-30, US-13)** `GET /claims` is staff-only, filters `market`, `type`, `status`, `assigneeId`, paged with `page` (>= 1) and `pageSize` (1 to 100, default 20); a larger `pageSize` or a bad filter value is 400, never silently clamped.
- **AC-14 (S6)** Every error is RFC 7807 `application/problem+json` with the bare problem `type` code, no stack trace, no exception text, no submitted values, with `correlationId`; unhandled errors are 500 `server-error` with a generic detail.
- **AC-15 (S32, ARCHITECTURE s9)** Every endpoint has a named policy (or explicit anonymous for `/health/**`); every non-GET endpoint has idempotency metadata; fallback policy requires authentication.
- **AC-16 (ADR-003, US-25)** `contracts/openapi/claims.yaml` equals the document generated from the running code (drift test) and merges into the Gateway document with `identity.yaml` without collision.
- **AC-18 (BR-79 to BR-87 at the HTTP edge)** A missing key is 400 `idempotency-key-missing`; a malformed key or several header values is 400 `idempotency-key-invalid`; a wrong-role caller gets 403 whether or not a key is sent; a byte-identical resend returns the identical stored bytes with `Idempotent-Replayed: true` on every route family; the same key with other bytes is 422; an in-flight duplicate is 409 with `Retry-After: 1`; expired or missing token is 401 before any lookup. The executor logic itself is tested in spec 2.
- **AC-17 (TASKS 4.2, 10)** Every role-policy 403 and every ownership 404 writes one structured security log event (actor, roles, route template, claimId if any, correlationId, outcome) and nothing else: no trail entry, no idempotency record, no body, no key. Correlation id is on every response and log line; `/health/ready` is 200 only after migration.

## 3. Domain model and invariants
No domain logic here. Invariants this layer owns: (1) identity and roles come only from the validated token (BR-11); no DTO has `status`, `assignedOfficerId`, `reserve`, `amount` on proposals or confirm, `makerId`, `checkerId`, `actorId`, `currency` (submit), `claimantId`; (2) a claimant view is a different C# type from a staff view, so masking cannot be a forgotten null; (3) a role is a coarse filter, ownership/assignment/maker rules are Application and Domain (ARCHITECTURE s9).

Pieces added here (interfaces only where a test seam or the layering needs one):
- `ActorFactory.From(ClaimsPrincipal)` builds `Actor(UserId = sub, Roles)`; role claim read as string or array (ADR-008); a non-GUID `sub` is a 401.
- `ICommandContextFactory.Create(HttpContext)` (Api) returns `CommandContext(Actor, TimeProvider.GetUtcNow(), correlationId, IIdGenerator)`.
- Queries (read side, `AsNoTracking`, `Select` projections, no N+1): `IClaimQueries.GetAsync(id, actor)`, `ListAsync(ClaimListFilter, paging, actor)`, `ITrailQueries.GetTimelineAsync(claimId, actor)`.
- Claimant status masking is spec 1's `ClaimantView.StatusFor(ClaimStatus)` (Domain, spec 1 AC-19: PendingDecisionApproval reads as UnderReview, Phase 2 PendingSettlementApproval as Approved). This spec only calls it from the claimant mapper; there is no second implementation.

## 4. API (REST via Gateway)
Routes are mapped with `MapGroup("/claims")` and `RequireAuthorization(policy)` per endpoint; `{id}` and `{pid}` use `:guid` (`/claims/mine` and `/claims/reference-data` are literal and win). Commands return 200 with the updated summary except submit (201). Each lambda is at most about 10 lines: bind, `ICommandContextFactory`, handler, map. "Idem." means `.WithIdempotency()` then `.WithValidation<TRequest>()` (order matters, below).

| Method and route | Policy | Request DTO (all fields client-settable) | Handler (spec 2) | Success | Errors (problem `type`, status) |
|---|---|---|---|---|---|
| `POST /claims` | Claimant | `SubmitClaimRequest { policyNumber, type, market, incidentDate, description, estimatedAmount, motorDetails? { vehicleRegistration, incidentLocation }, propertyDetails? { propertyAddress, damageType } }` (exactly the details object that matches `type`) | `SubmitClaimHandler` | 201 + `Location`, claimant summary | 400 `validation-error`; 422 `policy-not-found`, `market-mismatch`, `incident-outside-policy-period`, `incident-date-in-future` |
| `GET /claims/mine` | Claimant | query `page`, `pageSize` | `ClaimQueries.ListMine` | 200 page of claimant summaries | 400 |
| `GET /claims` | Staff | query `market`, `type`, `status`, `assigneeId`, `page`, `pageSize` | `ClaimQueries.List` | 200 page of staff summaries | 400 |
| `GET /claims/{id:guid}` | AnyUser | none | `ClaimQueries.Get` | 200 view by role | 404 `claim-not-found` |
| `GET /claims/{id:guid}/timeline` | AnyUser | none | `TrailQueries.GetTimeline` | 200 | 404 `claim-not-found` |
| `GET /claims/reference-data` | AnyUser | none | static | 200 | none (COULD) |
| `POST /claims/{id}/assign` | Officer | `{}` (`EmptyRequest`, unknown member is 400) | `PickUpClaimHandler` | 200 staff summary | 404; 409 `invalid-transition`, `claim-already-assigned` |
| `POST /claims/{id}/reassign` | Staff | `{ newOfficerId, reason }` | `ReassignClaimHandler` | 200 | 400; 403 `not-assigned-officer`; 409 `invalid-transition`; 422 `unknown-officer`, `same-officer` |
| `POST /claims/{id}/assessed-amount` | Officer | `{ assessedAmount, note }` | `AssessClaimHandler` | 200 | 400; 403 `not-assigned-officer`; 409 `invalid-transition`, `proposal-pending` |
| `POST /claims/{id}/info-requests` | Officer | `{ question }` | `RequestInformationHandler` | 200 | 400; 403; 409 `invalid-transition`, `open-info-request-exists`, `proposal-pending` |
| `POST /claims/{id}/info-responses` | Claimant | `{ response }` | `AnswerInformationRequestHandler` | 200 claimant summary | 400; 404; 409 `invalid-transition` |
| `POST /claims/{id}/decision-proposals` | Officer | `{ outcome, reasonCode?, rationale, justification? }` | `ProposeDecisionHandler` | 200 staff view, `openProposal` set | 400; 403; 409 `invalid-transition`, `proposal-pending`; 422 `reason-code-invalid`, `assessed-amount-required`, `payable-zero`, `justification-required` |
| `POST /claims/{id}/decision-proposals/{pid}/confirm` | Manager | none (any body is ignored and still fingerprinted) | `ConfirmDecisionHandler` | 200 | 403 `maker-cannot-check`; 409 `proposal-not-open`, `concurrency-conflict` |
| `POST /claims/{id}/decision-proposals/{pid}/send-back` | Manager | `{ reason }` | `SendBackDecisionHandler` | 200 | 400; 403 `maker-cannot-check`; 409 as confirm |
| `POST /claims/{id}/decision-proposals/{pid}/withdraw` | Officer | `{ reason }` | `WithdrawDecisionHandler` | 200 | 400; 403 `not-proposal-maker`; 409 as confirm |
| `POST /claims/{id}/corrections` | Staff | `{ referencedEntryId, reason, correctedStatement }` | `IssueCorrectionHandler` | 200 | 400; 403 `not-assigned-officer`; 422 `entry-not-found` |
| `POST /claims/{id}/settlement-proposals` | Officer | none | `RefuseSettlementCommandHandler` | none | 409 `phase-2-not-enabled` |
| `POST /claims/{id}/settlement-proposals/{pid}/confirm` and `/send-back` | Manager | none | same | none | 409 `phase-2-not-enabled` |
| `POST /claims/{id}/settlement-proposals/{pid}/withdraw` | Officer | none | same | none | 409 `phase-2-not-enabled` |
| `GET /health/live`, `GET /health/ready` | explicit anonymous | none | health checks | 200 | 503 |
Every command also returns 400 `idempotency-key-missing`/`-invalid`, 409 `idempotency-request-in-progress`, 422 `idempotency-key-reused` (spec 2), and 401/403 `forbidden` from the policy; every route can return 401 and 413 (64 KB body limit) and 500 `server-error`.

### 4.1 Pipeline order (outermost first)
Exception handler, correlation id, Serilog request logging, authentication, authorisation (with the custom result handler), `RequestBufferingMiddleware` (idempotent endpoints only), endpoint filters: **idempotency filter, then validation filter**, then the lambda. Why validation sits inside: ADR-009 step 4 runs validation within the transaction, after the key lookup, so a body with the wrong bytes for a used key answers 422 `idempotency-key-reused`, not a validation error, and a 400 simply rolls back and is not stored. A body that cannot be bound (unknown member, bad JSON, wrong type, bad enum) fails in model binding before any filter, returns 400 `validation-error` and is stored nowhere.

### 4.1a HTTP idempotency adapter (spec 2 `IdempotentCommandExecutor`)
- `.WithIdempotency()` adds endpoint metadata (read by the filter, `RequestBufferingMiddleware` and the S32 startup test) and the `IdempotencyFilter` (`IEndpointFilter`).
- **Key header:** read `Idempotency-Key` from the request; absent: 400 `idempotency-key-missing`; several header values, or not 8 to 128 characters of `A-Za-z0-9_-`: 400 `idempotency-key-invalid`. Neither is stored, trailed or logged with the key. This runs after authentication and the role policy, so a wrong-role caller gets 401/403 with or without a key (BR-79).
- **Scope and fingerprint:** actor id = token `sub`; canonical route = `METHOD ` + the matched route template filled with resolved values, GUIDs lower-case `D` (not the raw path); body bytes from the buffered stream (rewound after model binding, at most 64 KB); `RequestFingerprint.Compute(method, route, bytes)` from spec 2.
- **Execution:** `run` calls the rest of the pipeline (validation filter, lambda) and turns the typed result (`IStatusCodeHttpResult`, `IValueHttpResult`, `Created<T>.Location`, `ProblemHttpResult`) into a `StoredResponse` using the shared JSON options, so the first response and every replay are byte-identical; `refusalToResponse` is `ProblemResults.FromRefusal(...)` serialised the same way. The executor outcome maps to HTTP: `Executed`/`NotStored` write the response; `Replayed` writes it plus `Idempotent-Replayed: true` (`HeaderNames.IdempotentReplayed`); `InProgress` is 409 `idempotency-request-in-progress` with `Retry-After: 1`; `KeyReused` is 422 `idempotency-key-reused` with no stored content.
- `RequestBufferingMiddleware` calls `EnableBuffering()` only for endpoints that have the metadata; the Kestrel limit is 64 KB (413 above it).
- Logging: route template, outcome and status code only; never the key, hash or body (S34).

### 4.2 Policies and the security events
- Constants in `ClaimsPolicies`: `Claimant`, `Officer`, `Manager`, `Staff` (Officer or Manager), `AnyUser` (Claimant, Officer or Manager); roles from `Contracts.Roles`. Fallback policy: authenticated user. `System` and `Service` satisfy none of them (S36); the `Service` policy is added with the first Phase 2 internal endpoint (Q5). A dual-role token satisfies Officer and Manager; maker-not-checker stays a domain rule.
- `ForbiddenResultHandler : IAuthorizationMiddlewareResultHandler`: on `Forbidden` it logs the security event `RolePolicyDenied` (LoggerMessage, structured fields: `actorId`, `roles`, `routeTemplate`, `claimId` from route values, `correlationId`, `outcome`), returns 403 `forbidden` problem, and writes nothing to the database. On `Challenged` it returns 401 `unauthorized` problem. It never reads the body or the `Idempotency-Key`, so the key is not even parsed before authorisation succeeds (BR-79).
- Ownership: query and handler return the untrailed refusal `claim-not-found` for a missing claim and for a claim the claimant does not own (one SQL predicate `claimant_id = @sub` for claimants, so there is one code path and one response). The problem mapper logs `OwnershipDenied` (same fields) for 404s on claim routes. Staff are not scoped by market (Q4).

### 4.3 DTOs, JSON and validation
- One `JsonSerializerOptions` for HTTP and for the stored idempotent bytes (spec 2 uses the same instance): camelCase, enums as strings, `UnmappedMemberHandling.Disallow`, strict number handling, `DateOnly` for dates, `DateTimeOffset` UTC. Money is a JSON number `amount` with a separate `currency` string, never a double in code.
- Request types are `sealed record` with get-only properties and a `required` marker only where mandatory; validators are `AbstractValidator<T>` per request in `Api/Validation/`. Messages never include the submitted value (personal data). Errors return `errors: { field: [messages] }`.
- Rules (edge duplicates of domain checks, domain still enforces): ids non-empty GUIDs; `type`/`outcome`/`reasonCode`/`market` valid enum members; amounts `> 0`, at most 2 decimals, at most `Claims:Validation:MaxAmount` (default 1,000,000,000,000, S7, fits `numeric(18,2)` and IDR); `reason`, `rationale`, `justification` trimmed length >= 10 where the BR requires it; maximum lengths (`description` 2,000, `question` 2,000, `response` 4,000, `rationale` 2,000, `justification` 2,000, `reason` 1,000, `note` 1,000, `correctedStatement` 1,000, `vehicleRegistration` 20, `incidentLocation` 200, `propertyAddress` 300, `damageType` 100); `incidentDate` not before 1990-01-01; details object matches `type` and the other one is absent. The future-date and policy-period rules stay in the domain (they need the policy and the clock).
- Views and contract schemas, one per C# type (no shared schema with optional staff fields): `ClaimantClaimView` = `ClaimsClaimantClaimView` (id, claimNumber, type, market, currency, status masked, incidentDate, description, estimatedAmount, `approvedAmount` only when Approved, `openInformationRequest { id, question, dueAt }` when open, submittedAt, updatedAt); `StaffClaimView` = `ClaimsStaffClaimView` (the same fields with the real status plus claimantId, policyId, assignedOfficerId, assessedAmount, netPayable, reserve, payable, and `openProposal { id, outcome, amount, currency, reasonCode, rationale, justification, makerId, proposedAt }` or null, so the checker sees exactly what they confirm, BR-34). There is no `openProposalId`; propose returns the staff view and the new proposal id is `openProposal.id`. Pages: `ClaimsClaimantClaimPage` and `ClaimsStaffClaimPage`. Claimant-only routes (`/claims/mine`, info-response, submit) declare the claimant schemas; staff routes declare the staff schemas; `GET /claims/{id}` and `GET /claims/{id}/timeline` declare `oneOf` the two (the generated TypeScript is a union). A claimant response is always built from `ClaimantClaimView`, which has no staff properties (S37 asserts on the serialised JSON and against the contract).
- Timeline: claimants get `ClaimantTimeline.Project(...)` output (spec 1, pure: `id, occurredAt, summary`, ordered by `occurredAt` then `id`) as `ClaimsClaimantTimelineItem`; staff get `StaffTimelineEntry` = `ClaimsStaffTimelineEntry` (adds `eventType, fromStatus, toStatus, actorId, actorRole, reason, visibility, proposalId, makerId, checkerId, referencedEntryId, reserveBefore, reserveAfter, correctedByEntryId`). The staff query orders by `(occurred_at, id)` in SQL using the `(claim_id, occurred_at, id)` index (never an in-memory GUID sort) and computes `correctedByEntryId` from Correction rows in the same query.
- Lists: `ClaimsClaimantClaimPage` / `ClaimsStaffClaimPage { items, page, pageSize, totalCount }`, order `submitted_at DESC, id`; filters use the `(status, market)`, `assigned_officer_id` and `claimant_id` indexes; `pageSize` above 100 or `page` below 1 is 400 `validation-error` (never clamped); staff see all markets in Phase 1 (the token `market` claim is display-only).
- `reference-data` (COULD): markets with currency, claim types, reject codes with labels, from Domain constants; no database call.

### 4.4 Problem details (`ProblemResults`, owned here; the executor receives the refusal mapping as a delegate)
`application/problem+json` with `type` (bare code, as the Gateway's `ProblemTypes`), `title`, `status`, `detail`, `correlationId`, and `errors` for 400 only. Factory methods: `FromRefusal(DomainRefusal)`, `IdempotencyKeyMissing`, `IdempotencyKeyInvalid`, `IdempotencyKeyReused`, `IdempotencyRequestInProgress`, `Validation(errors)`, `Unauthorized`, `Forbidden`, `NotFound`, `PayloadTooLarge`, `ServerError`. `ClaimsExceptionHandler : IExceptionHandler` maps: `DomainRefusalException` reaching it (non-idempotent or unfiltered path) to its refusal; `BadHttpRequestException` (bad JSON, unknown member; 413 for the body limit) to 400/413 with no inner message; anything else to 500 `server-error`, logged with `correlationId`, response generic. Every `DomainRefusals` constant must map to a status here and be documented in `claims.yaml` (test).

## 5. Data
No tables or migrations (spec 2). Reads only. Read-model records live in Application (`ClaimRecord`, `TimelineRecord`) and are projected in SQL, never entity graphs. No query touches another service's data. Configuration keys added here: `Claims:Validation:MaxAmount`, Kestrel `MaxRequestBodySize` 64 KB (set in code from a constant).

## 6. Events
None published or consumed here. Handlers call the spec 2 unit of work, which writes the outbox rows. Settlement stubs, corrections, refusals and replays raise no event.

## 7. Claim progress trail
Endpoints add no entries themselves; the entries per command are the table in spec 2 section 7 (decision log 15). The read side projects them: claimants get Public entries only through `ClaimantTimeline.Project`; staff get both audiences with `visibility`. A pending proposal and a send-back appear to the claimant only as the single "Decision under review" entry written at the first proposal (S37).

## 8. Security
Authorisation matrix rows implemented (ARCHITECTURE s9 table; SECURITY-REQUIREMENTS section 4): every row of the Claims table in section 4 above; `/claims/{id}/payee-details` and `/internal/**` are not served here. Threats mitigated: BOLA/IDOR on claims, timeline and info-response (single predicate, same 404); function-level authorisation (named policies, default deny, startup coverage test); mass assignment (closed DTOs, `Disallow`); information disclosure (claimant view type, masked status, Public-only projection, problem details without internals, no echo of submitted values); injection and absurd input (validators, parameterised queries only, amount cap, 64 KB body, page-size cap); trail flooding by role-policy 403 (log only, ADR-009); header spoofing (identity from the token only, never a header).
Never logged or returned: tokens, `Idempotency-Key`, request/response bodies, answer text, description, names. Logs carry ids, route templates, outcomes, correlation id. Swagger/OpenAPI UI is Development-only; `claims.yaml` is a committed file, not served by Claims in production.
Security tests (assigned in section 10): S1 to S16, S32 to S37.

## 9. Dependencies and sequencing
Needs spec 1 (REVIEWED) and spec 2 (all handlers, `IOfficerDirectory`, `IdempotentCommandExecutor`, store, recorder, `RequestFingerprint`); existing Claims host with JWT validation (`AuthenticationSetup`); `Contracts` (`Roles`, `HeaderNames`, `DevSeed`, `Markets`); **Contracts additions in this spec: `HeaderNames.IdempotentReplayed = "Idempotent-Replayed"`** (ADR-009; `Retry-After` is the standard header and needs no constant). New packages (central props): `Microsoft.AspNetCore.Mvc.Testing` (tests), `FluentValidation` (+ DI extensions), `Serilog.AspNetCore`/`Serilog.Formatting.Compact` (already used by the Gateway), `Swashbuckle.AspNetCore` (spec generation for the drift test and Development UI), `Microsoft.OpenApi.Readers` (drift test), then lock files.
- **Gateway coordination (files owned separately):** the Gateway aggregator merges `contracts/openapi/*.yaml` and fails on anything other than `components.schemas`, on a schema name or path claimed twice, and drops `/internal/**` and `/health/**`. So `claims.yaml` must: use only `components.schemas`, prefix every schema `Claims`, declare `Idempotency-Key` **inline** as a required header parameter on each command (never `components.parameters`), declare `Idempotent-Replayed`, `Location` and `Retry-After` response headers inline, declare the four idempotency problem types, use paths equal to the service paths, contain no security schemes, and exclude health. Request to the Gateway owner: one test that merges `identity.yaml` and `claims.yaml` without collision and keeps the Gateway image build context including `contracts/openapi/` (Q6).
- **Contract authoring order (contract first, ADR-003):** write `claims.yaml` before the endpoints (it unblocks spec 4 and the Angular client), then generate the document from code in a test and fail on drift. The drift test compares parsed models (paths, operations, parameters, response codes, schema names, properties, required), not text.
- Gateway tests (owner: Gateway developer): add `Aggregator_WithClaimsAndIdentityContracts_MergesWithoutCollision` once `claims.yaml` lands; the Gateway build context already copies `contracts/openapi`.
- Unblocks: spec 4 (frontend idempotency), Reporting/Notification demos (through seeded and live events), the walkthrough.

### Compose and Dockerfile handoff (`devops-engineer`)
`/health/ready` stays DB-only; the `outbox` Degraded health check is registered by the publisher slice (`messaging-engineer`), not here, so the health wiring does not conflict. Builds on the spec 2 list (which already carries `Settlement__Enabled` and `Officers__KnownIds`). Additions: none beyond the `healthcheck` using `/health/ready`, which now also checks the database context; nothing else is secret. The Dockerfile exists and is unchanged. The Gateway image must contain `contracts/openapi/claims.yaml`. README (US-25): how to regenerate the Angular client from the Gateway document.

## 10. Test plan
Unit tests for mappers/validators/projection; integration tests with `WebApplicationFactory` + Testcontainers PostgreSQL 16 (spec 2 fixture), test tokens from `tests/TestSupport` (`TestTokens`, `TestKeyPair`), log capture with `CapturingLogSink`. The executor logic (atomicity, races, I1 to I3, I5 to I8, S33 at handler level) is tested in spec 2 without HTTP; this spec tests the HTTP edge (I4, S12, S34, S35, replay smoke per route family).

| Criterion / risk | Type | Test name |
|---|---|---|
| AC-1 | integration | `SubmitClaim_Valid_Returns201WithLocationAndIdAndClaimNumber` |
| AC-1 | integration | `SubmitClaim_BodyContainsCurrencyOrStatus_Returns400` |
| AC-2 | unit (theory) | `SubmitClaimValidator_InvalidInput_ReportsFieldErrorsWithoutValues` |
| AC-2 | integration (theory) | `SubmitClaim_PolicyOfOtherClaimantMarketMismatchPeriodOrFutureDate_Returns422NotStored` |
| AC-2 S7 | integration | `SubmitClaim_AbsurdOrNegativeAmountOrOversizedBody_Returns400Or413` |
| AC-3 S10 | integration | `Assign_TwoOfficersAtOnce_OneOkOneConflict` (HTTP smoke; the race itself is a spec 2 test) |
| AC-3 | integration | `Assign_NonEmptyUnknownMember_Returns400` |
| AC-4 | integration | `Reassign_ManagerToKnownOfficer_Returns200` |
| AC-4 | integration | `Reassign_UnknownOfficerOrSameOfficer_Returns422WithDistinctTypes` |
| AC-4 | integration | `Reassign_OfficerNotAssignee_Returns403RefusedAndStored` |
| AC-5 | integration | `AssessedAmount_AssignedOfficer_ReturnsReserveAndNetPayable` |
| AC-6 | integration | `InfoRequestAndResponse_FullCycle_ReturnsToUnderReview` |
| AC-6 S2 | integration | `InfoResponse_OtherClaimant_Returns404` |
| AC-7 | integration | `ProposeDecision_Valid_ReturnsOpenProposalId` |
| AC-7 S5 | integration | `ProposeDecision_BodyContainsAmountOrMakerId_Returns400` |
| AC-8 S16 | integration | `Confirm_BodyWithAmountOrOutcome_IgnoredAndAppliedAsProposed` |
| AC-8 | integration | `SendBackAndWithdraw_ReasonUnderTenCharacters_Returns400NotStored` |
| AC-9 | integration | `Correction_ReferencedEntryOnOtherClaim_Returns422AfterResourceRuleCheck` |
| AC-9 | integration | `Correction_NonAssignedOfficerWithUnknownEntry_Returns403NotEntryNotFound` |
| AC-10 | integration (theory 4 routes) | `SettlementStub_AnyStatus_Returns409Phase2NotEnabledRefusedAndReplayable` |
| AC-11 S2 | integration | `ClaimantReads_OtherClaimantsClaim_DetailAndTimelineReturnSame404AsMissing` |
| AC-11 | integration | `ClaimantList_ShowsOnlyOwnClaims` |
| AC-11 BR-33 | integration | `ClaimantMapper_PendingDecisionApproval_ReturnsUnderReviewViaClaimantViewStatusFor` |
| AC-11 S37 | integration | `ClaimantViews_ThroughPendingSendBackReproposalWithdraw_AlwaysUnderReviewOneDecisionUnderReviewEntryNoStaffFields` |
| AC-11 | unit | `ClaimantClaimView_Type_HasNoStaffProperties` (reflection) |
| AC-12 | integration | `StaffTimeline_ReturnsPublicAndInternalWithVisibilityAndCorrectedBy` |
| AC-13 | integration (theory) | `ListClaims_FiltersMarketTypeStatusAssignee_ReturnsMatchingPage` |
| AC-13 S7 | integration | `ListClaims_PageSizeAboveMaxOrPageBelowOne_Returns400` |
| AC-13 | integration | `ListClaims_AsClaimant_Returns403` |
| AC-14 S6 | integration | `Errors_400_403_404_409_422_500_AreProblemJsonWithoutStackTraceOrSubmittedValues` |
| AC-14 S6 | integration | `UnhandledException_Returns500GenericAndLogsCorrelationIdOnly` |
| AC-14 | unit | `ClaimsExceptionHandler_BadHttpRequest_Maps400Or413WithoutInnerMessage` |
| AC-15 S32 | integration (startup) | `Endpoints_EveryEndpoint_HasNamedPolicyOrExplicitAnonymousHealth` |
| AC-15 S32 | integration (startup) | `Endpoints_EveryNonGetEndpoint_HasIdempotencyMetadata` |
| AC-15 S32 | integration | `Command_WithoutKey_Returns400AndEntryCountUnchanged` |
| AC-15 | integration (startup) | `Endpoints_IdSegments_UseGuidRouteConstraint` |
| AC-15 | unit | `DomainRefusals_EachConstant_MapsToStatusAndIsDocumentedInContract` |
| AC-16 | integration | `Contract_GeneratedDocument_MatchesCommittedClaimsYaml` |
| AC-16 | unit | `Contract_ClaimsYaml_UsesOnlySchemaComponentsPrefixedClaimsAndInlineIdempotencyKey` |
| AC-16 | integration (Gateway tests, Q6) | `Aggregator_WithClaimsAndIdentityContracts_MergesWithoutCollision` |
| AC-17 | integration | `RolePolicyDenied_LogsOneSecurityEventAndWritesNoTrailOrKeyRecord` |
| AC-17 | integration | `OwnershipDenied_LogsOneSecurityEventWithoutBodyOrKey` |
| AC-17 | integration | `Response_CarriesCorrelationHeaderAndLogsCarryCorrelationId` |
| AC-17 | integration | `HealthReady_AfterMigration_Returns200Anonymous` |
| S1 | integration (theory) | `AnyEndpoint_NoTokenMalformedExpiredWrongAudienceWrongIssuerWrongKeyAlgNoneHs256WithPublicKey_Returns401` |
| S3, S12 | integration (theory per command route and wrong role) | `Command_WrongRole_Returns403LogsSecurityEventNoEntryNoRecordResendExecutesAgain` |
| S4 | integration | `Assess_ProposeWithdraw_NonAssignedOfficer_Returns403Refused` |
| S5 | integration (theory per DTO) | `Command_SpoofedServerOwnedFields_Returns400AndStateUnchanged` |
| S9 | integration | `InvalidTransitions_ApproveSubmittedConfirmNonPending_Return409` |
| S11 | integration | `Confirm_DualRoleMaker_Returns403MakerCannotCheckOneRefusedEntry` |
| S13, S13b | integration | `Propose_Withdraw_NonAssignedOrNonMaker_Returns403`; `ClaimantTimeline_PendingAndAfterSendBack_ShowsOnlyDecisionUnderReview` |
| S14 | integration | `Confirm_StaleProposalId_Returns409NothingApplied` |
| S15 | integration | `Confirm_TwoManagersAtOnce_OneWins` (spec 2 race test through the real route) |
| S36 | integration (theory per route) | `SystemOrServiceRoleToken_EveryClaimsEndpoint_Returns403NoEntry` |
| S33 | integration | `InfoResponse_OtherClaimantWithFirstClaimantsKey_Returns404ThroughRealRoute` |
| S34 | integration | `Logs_AfterInfoResponseAndSubmit_ContainNeitherAnswerTextNorKeyNorHash`; `Command_KeyReused_ResponseBodyContainsNoStoredContent`; `Replay_ExpiredRecord_ExecutesAsNewViaHttp` |
| S35 | integration | `Replay_ExpiredOrMissingToken_Returns401WithoutReplayHeader` |
| AC-18 I4 BR-79 | integration (theory: none, 7 chars, 129 chars, space, two header values) | `Command_MissingOrMalformedKey_Returns400WithMissingOrInvalidTypeAndWritesNothing` |
| AC-18 BR-79 | integration | `OfficerOnlyUserConfirmWithoutKey_Returns403Not400` |
| AC-18 S12 | integration | `RolePolicy403_LeavesNoIdempotencyRecordAndTrailUnchanged` |
| AC-18 BR-82 | integration (theory, one per route family: create, body command, empty-body command, claimant command, stored refusal, correction) | `Command_SameKeyAndBytesTwice_ReplaysIdenticalBytesWithReplayHeader` |
| AC-18 BR-83 | integration | `Command_SameKeyDifferentBody_Returns422KeyReused`; `Command_StoredKeyThenInvalidBodySameKey_Returns422KeyReusedNot400` (store k with a valid body, then a body that binds but fails validation with k) |
| AC-18 BR-86 | integration | `Command_InvalidBodyTwiceSameKey_Returns400BothTimesNothingStored` |
| AC-18 BR-85 | integration | `Command_SameKeyInFlight_Returns409WithRetryAfterOne` |
| AC-18 | unit | `IdempotencyFilter_StoredBytesEqualFirstResponseBytes` |
| AC-18 | unit | `RequestBufferingMiddleware_EnablesBufferingOnlyForIdempotentEndpoints` |
| AC-18 | unit | `ProblemResults_IdempotencyTypes_HaveExpectedStatusAndHeaders` |
| S6 PII | integration | `Logs_AfterSubmitAndInfoResponse_ContainNoDescriptionAnswerTokenOrKey` |

## 11. Implementation plan
Each step is a commit with tests; estimate about 2 hours 40 minutes of the 5-hour cap, after spec 2.
1. `claims.yaml` (all routes, schemas, inline headers) and the drift/merge-shape tests, red until step 5 (25 min).
2. Policies, `ActorFactory`, `ICommandContextFactory`, `ForbiddenResultHandler`, correlation middleware, Serilog, exception handler, `ProblemResults` additions, health check (30 min).
3. DTOs, validators, validation filter, JSON options, `Disallow` tests (25 min).
4. `IdempotencyFilter` adapter, `.WithIdempotency()`, `RequestBufferingMiddleware`, key validator, HTTP idempotency tests (30 min).
5. Command endpoint group with `.WithIdempotency()`, the S32 startup tests, one replay smoke per route family (25 min).
6. Queries and views (detail, lists, timeline, claimant masking), reference-data (35 min).
7. Security matrix tests S1 to S16, S36, S37 (20 min). 8. CODING-GUIDELINES section 15 checklist; hand the compose notes to `devops-engineer`.
Folders: `Api/{Endpoints,Validation,Problems,Security}`, `Application/{Queries,Views}`, `Infrastructure/Queries`. Configuration keys in section 5.
**Cut line** (technical advice; `product-owner` decides): first `GET /claims/reference-data` (COULD), then the `correctedByEntryId` flag and staff list filter breadth (keep `status` and `assigneeId`); the settlement stub routes shrink to the single `POST settlement-proposals` only if the startup coverage test is kept. Never cut: policies and S32, closed DTOs, claimant view separation, problem details, timeline masking, `claims.yaml`.

## 12. Risks, assumptions, open questions
Risks:
1. The two views must not drift from their contract schemas; mitigated by the drift test (one C# type per schema), the reflection test and S37 on serialised JSON.
2. Endpoint filter order (idempotency before validation) is easy to invert by accident; the test `Command_StoredKeyThenInvalidBodySameKey_Returns422KeyReusedNot400` proves the lookup runs first. A body that cannot be bound (bad JSON, unknown member) is 400 from model binding even for a used key; nothing is executed or stored, which is acceptable.
3. Unknown-member rejection (`Disallow`) also rejects harmless extra fields from a newer client; accepted and stated in the contract.
4. Swashbuckle output for `[JsonUnmappedMemberHandling]`, optional staff fields and header responses may need schema filters to match the hand-written file; budget in step 1.
5. Spec 1 is REVIEWED with amendments A1 to A5; names used here (`ClaimantTimeline.Project`, `DomainRefusals`) must still be checked against its final text when coding.

Assumptions: staff see all markets (token `market` claim not used for scoping); a claimant never holds a staff role (BR-78); `GET` endpoints are never idempotent and need no key; error `detail` strings are fixed texts or ids/codes; the Gateway forwards `Authorization`, `Idempotency-Key` and sets `X-Correlation-Id` (identity-gateway spec), and Claims generates one if it is missing or not a GUID.

Open questions:
- Closed by the architect review (section 13): propose response and two schemas (change 3), masking in Domain (change 1), staff see all markets (Q4), `Service` policy registered with the first Phase 2 internal endpoint (Q5), Gateway merge test (Q6), 400 for `pageSize` above 100 or `page` below 1 (Q7).
- **Q8 (product-owner)** Claimant view content: `approvedAmount` after approval and `openInformationRequest.question` while InfoRequested are included; confirm they are claimant-visible (both already appear in Public timeline text).
- **Q9 (product-owner)** `GET /claims/reference-data` stays COULD and is cut first.

## 13. Review record
| Reviewer | Date | Verdict (REVIEWED / CHANGES REQUESTED) | Notes |
|---|---|---|---|
| architect | 2026-10-28 | CHANGES REQUESTED | A good fit with the new ownership: no handlers here; a thin adapter over the spec 2 executor; validation inside the filter (ADR-009 step 4); the role-policy result handler only logs; one 404 predicate for ownership; separate claimant and staff C# types; the inline `Idempotency-Key`; contract first with a parsed-model drift test; S32 and S36 in place. Changes 1 to 3 are contract corrections, and change 4 fixes a wrong test. All are small. |

**Required changes (numbered).**
1. **Q3: use spec 1's `ClaimantView.StatusFor`** (Domain, spec 1 AC-19, REVIEWED). Delete `ClaimantStatusView` from section 3. Rename `ClaimantStatusView_PendingDecisionApproval_ReadsUnderReview` to an API-level assertion, or drop it, because the unit test lives in spec 1. Why Domain: masking is the BR-33 rule and sits beside `ClaimantTimeline`; Application would give it two homes.
2. **Submit takes `policyNumber`, not `policyId`** (US-01: "Given a valid policy number"). No endpoint gives a claimant a policy id. The handler resolves the policy by the unique `policy_number`; an unknown number or another person's policy gives the same 422 `policy-not-found`. Update the DTO, the validator (trimmed, at most 30 characters), AC-1/AC-2 and the tests. Spec 2's Submit handler takes the number (tell its author; this is a one-line change there). ARCHITECTURE section 9 is updated.
3. **Q2: two contract schemas, one per C# view.** `ClaimsClaimantClaimView` and `ClaimsStaffClaimView`, with pages `ClaimsClaimantClaimPage` and `ClaimsStaffClaimPage`; timeline items `ClaimsClaimantTimelineItem` and `ClaimsStaffTimelineEntry`. Claimant-only routes declare the claimant schema and staff routes the staff schema. `GET /claims/{id}` and `/timeline` declare `oneOf` the two (typescript-angular generates a union type). Drop `openProposalId`. The staff view carries `openProposal { id, outcome, amount, currency, reasonCode, rationale, justification, makerId, proposedAt }` or null, so the checker sees exactly what they confirm (BR-34), and propose returns the staff view with `openProposal.id`. Why not one schema with optional staff fields: the generated claimant type would then list staff fields, the drift test would need schema filters to match one schema to two C# types (your risk 4), and S37 could not be checked against the contract. ARCHITECTURE section 9 and ADR-003 are updated.
4. **The filter-order test is wrong (risk 2, test `Command_BadBodyTwiceSameKey_SecondIs422NotTwo400s`).** A 400 is not stored, so sending the same bad body twice with one key gives 400 twice, which is correct (BR-86). The real order test: send a valid body with key k and get 200 (stored); then send a body that binds but fails validation, with key k, and expect 422 `idempotency-key-reused`. That proves the lookup runs before validation. Rename it to `Command_StoredKeyThenInvalidBodySameKey_Returns422KeyReusedNot400`. Add one sentence to 4.1: a body that cannot be bound (bad JSON, unknown member) is 400 from model binding even for a used key. Nothing is executed or stored, so this is acceptable.
5. **Remove risk 6 and Q1:** the confirm row now matches decision log 16 (checked: Phase 1 caps per claim only, with no lock and no re-check; the Phase 2 text is labelled).
6. **Health (ADR-004 amendment):** `/health/ready` stays DB-only. The `outbox` Degraded check is registered by the publisher slice (`messaging-engineer`), not here; note it under Dependencies so the health wiring does not conflict.

**Architect answers.**
- **Q1:** confirmed (change 5).
- **Q2:** change 3.
- **Q3:** change 1 (Domain).
- **Q4 (orchestrator default):** no architectural objection. Staff see all markets in Phase 1, and the token `market` claim is display-only. This is recorded as a trade-off in ARCHITECTURE section 12 and a gap in SECURITY-REQUIREMENTS section 6. The BA may still add a rule later; it would be one predicate in the list and detail queries.
- **Q5:** agreed. Register the `Service` policy with the first Phase 2 internal endpoint, not before (no dead code). S36 proves `System` and `Service` tokens get 403 today, because no policy lists them.
- **Q6:** the build context is already done: `src/Gateway/Dockerfile` line 23 copies `contracts/openapi` (checked). The merge test `Aggregator_WithClaimsAndIdentityContracts_MergesWithoutCollision` belongs in the Gateway tests (owner `backend-developer`, Gateway). Add it once `claims.yaml` lands in step 1. Keep it in your Dependencies list, not your test table.
- **Q7:** confirmed: 400 `validation-error` for `pageSize > 100` or `page < 1`. Why not clamp: a silently shorter page lets a client believe it has everything, and an explicit 400 fits the closed-DTO, reject-don't-guess rule (S5, S7).
- Q8 and Q9 are for `product-owner`. |
| architect (final) | 2026-10-28 | REVIEWED | Checked only changes 1 to 6, and all are in. (1) `ClaimantView.StatusFor` from Domain. (2) Submit DTO takes `policyNumber`. (3) Two schemas per C# view, `oneOf` on the two role-dependent GETs, `openProposal` object and no `openProposalId`. (4) The test is `Command_StoredKeyThenInvalidBodySameKey_Returns422KeyReusedNot400`, plus the unbindable-body sentence. (5) Risk 6 and Q1 removed. (6) Outbox health check left to the publisher slice. **Q8 and Q9 are closed by the product-owner** (TASKS 4.4 and user-stories "Confirmed decisions"). Q8: the claimant view includes `approvedAmount` only once Approved and `openInformationRequest` (question, due date) only while InfoRequested; section 4.3 already matches. Q9: `reference-data` is COULD and cut first; the section 11 cut line already matches. The author may delete the two "open" lines in section 12 when coding. Non-blocking: the PO's cut line (TASKS 4.4) also allows merging `/claims/mine` into `GET /claims` and deferring the settlement stubs. If either is used, update `claims.yaml` and the S32 test in the same commit, and keep the claimant ownership predicate on the merged route. Coding may start once spec 2 is in place (strict order 1, 2, 3). |
