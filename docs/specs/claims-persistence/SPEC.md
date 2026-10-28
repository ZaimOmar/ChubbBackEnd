# SPEC: Claims persistence, trail storage and idempotency  (service: Claims)

Spec 2 of the Claims specs (`docs/specs/README.md`). Builds on spec 1 (`claims-domain`, aggregate, `TrailEntry`, `DomainRefusal`, `PendingEvents`) and is wired to HTTP by spec 3. Code goes under `src/Claims/Application/` and `src/Claims/Infrastructure/` only: this spec has **no HTTP code** (the `IdempotencyFilter` adapter, request buffering, key-format validation and `ProblemResults` are spec 3). The outbox **publisher** is not in this slice (`messaging-engineer`); this slice only writes outbox rows.

| Field | Value |
|---|---|
| Author | backend-developer (outbox write side reviewed with `messaging-engineer`) |
| Status | REVIEWED (architect final check 2026-10-28; see section 13) |
| Phase | P1 build (Phase 2 tables `processed_events`, payee and payment columns are not created; Phase 2 columns on `claims` are created nullable) |
| Stories (US-xx) | US-01 (submit, claim number), US-06 (pick-up race), US-18 (immutable trail), US-22 (refused attempts), US-25 (migrations and seed on startup), US-26 to US-29 (assess, propose, confirm, send back), US-31 (withdraw), US-04 (info response replay), US-03/US-20 (Public/Internal storage) |
| Business rules (BR-xx) | BR-04 (entry counts, decision log 15), BR-05, BR-06 (immutable), BR-07 (atomic), BR-25 (claim number), BR-26 (one winner), BR-38 (concurrent confirm), BR-79 to BR-87 (idempotency), BR-22 (reserve before/after stored on the trail row) |
| Tasks (docs/planning/TASKS.md) | 4.1 (EF migrations, seed ~30 claims, indexes, rowVersion), 4.2 (append-only `claim_history`, one-transaction rule, entry counts, Refused entries), 4.5 (outbox rows, not the publisher), 4.6 (idempotency, all nine MUST lines), 8 (trail tests, concurrency tests, I1 to I8, S23/S33 to S35), 9 (compose and seed on startup) |
| Related ADRs | ADR-004 (outbox, `xmin`), ADR-005 (amended: refused entry after savepoint), ADR-009 (mechanics, accepted), ADR-010 (payload records, outbox `sequence`) |

**Build scope, Part A (backend-developer).** Implemented: `ClaimsDbContext` (snake_case, `xmin`, no retry), three migrations (`InitialSchema`, `ClaimHistoryAppendOnly`, `OutboxAndIdempotency`), `EfClaimRepository`, `EfClaimUnitOfWork` (concurrency translation), `OutboxWriter`, `PostgresOutboxStore`, `UuidV7IdGenerator`, `DomainRefusalException`, startup migration, tests in `tests/Claims.Persistence.Tests`. Deviations: (1) `claim_history.referenced_entry_id` is checked by an INSERT trigger, not a foreign key, because a key check needs the UPDATE privilege that the REVOKE removes (verified: it failed with permission denied); the proposal composite FK stays. (2) `information_requests` is deferred: the domain has no `InformationRequest` entity yet. (3) EF adds a `policy_id` FK index (convention). (4) `EventEnvelope` and `EventJson` were added to `Contracts` per ADR-010 (architect to confirm). (5) The migration at startup is skipped with a warning when `ConnectionStrings:ClaimsDb` is absent, so Hosts.Tests start without a database. (6) Minimal domain edits for EF: private parameterless constructors, `Claim.Type` stored, `Policy` money and currency from backing fields. Not built yet: handlers, executor, seeder.
Status: Part A implemented; Part B open.

## 1. Purpose and scope
Persist the Claims aggregate and make every command **one atomic unit**: claim change + trail entries + outbox rows + idempotency record commit together or not at all (BR-07, BR-84). Enforce the trail as append-only in the database, make pick-up and confirm race-safe with `xmin`, and make resends harmless with an HTTP-free `IdempotentCommandExecutor` that spec 3 adapts to HTTP. Seed realistic data through the same command path so Reporting and Notification do not start empty.

**In scope:**
- `ClaimsDbContext`, entity configurations, three reviewed reversible migrations (including the hand-written trigger, `REVOKE` and their `Down`), claim-number sequence.
- Application interfaces and Infrastructure implementations: `IClaimRepository`, `IClaimUnitOfWork`, `IOutboxWriter`, `ICommandTransaction`, `IIdempotencyStore`, `IRefusedAttemptRecorder`, `IOfficerDirectory`, `IOutboxStore`, `DomainRefusalException`, `UuidV7IdGenerator`.
- **All** thin Application command handlers (command side = this spec; HTTP and read side = spec 3): Submit (takes the policy **number**, resolved through the unique `policy_number`; an unknown number or another person's policy gives the same 422 `policy-not-found`), PickUp, Reassign (with `IOfficerDirectory`, raises `unknown-officer`), Assess, RequestInformation, AnswerInformationRequest, ProposeDecision, ConfirmDecision, SendBackDecision, WithdrawDecision, IssueCorrection (checks the referenced entry exists on the same claim through `IClaimRepository`, else `entry-not-found`) and the settlement-stub refusal handler (reads `Settlement:Enabled`).
- `IdempotentCommandExecutor` (Application, HTTP-free; lock, lookup, replay/reuse/expiry, savepoint, handler, storability, Refused entry, insert, commit) and the `Idempotency:Retention` options.
- Development seeder (`ClaimsSeeder`), the list of what `devops-engineer` needs for the Claims compose entry, Testcontainers test plan.

**Out of scope / not now:** the outbox publisher and Kafka (messaging-engineer); endpoints, DTOs, FluentValidation validators, role policies, `IdempotencyFilter` adapter, `RequestBufferingMiddleware`, key-format validator, `ProblemResults`, timeline and list queries, `claims.yaml` (spec 3); `processed_events` and every Finance behaviour (Phase 2); the expired-key cleanup job (COULD, correctness does not depend on it, BR-87); encryption of stored bodies (production gap, ADR-009); rate limiting.

## 2. Acceptance criteria
Persistence-level versions of the story criteria; each maps to tests in section 10 (ids AC-n).
- **AC-1 (BR-07, BR-84, US-18)** A successful command leaves the claim change, its trail entries, its outbox rows and its idempotency record, or none of them. A forced failure after the trail insert (and one after the outbox insert, and one at the idempotency insert) leaves none of the four; a retry with the same key then succeeds once.
- **AC-2 (BR-04, decision log 15, TASKS 4.2)** Entry counts per command are exactly those of section 7: first propose +2, confirm +2, send-back +1 (Internal only, no Public companion), withdraw +1, re-proposal +1, all other commands +1, a refused attempt +1 Internal, a replay 0.
- **AC-3 (BR-06, S23)** `UPDATE`, `DELETE` and `TRUNCATE` (also `TRUNCATE claims CASCADE`) on `claim_history` fail at the database, once through the privilege (`REVOKE`) and, with the privilege re-granted, through the triggers. A `Correction` is a new row and leaves the original byte-identical.
- **AC-4 (BR-26, US-06)** Two officers picking up the same Submitted claim at the same time (different keys): exactly one 200, the other 409 `claim-already-assigned` (or `invalid-transition` if it arrives after the commit), one `Assigned` entry, one Refused entry, one `ClaimAssigned` outbox row.
- **AC-5 (BR-38, US-28)** Two managers confirm the same proposal at the same time (different keys): exactly one 200, one 409 (`concurrency-conflict` or `proposal-not-open`) plus exactly one Refused entry stored under the loser's own key; one confirm entry pair, one `ClaimApproved` row, money applied once. Same for confirm against withdraw.
- **AC-6 (BR-25)** Claim numbers are unique and contiguous per market and year under 20 concurrent submits; a rolled-back submit leaves no gap; a replay does not advance the sequence.
- **AC-7 (BR-80 to BR-87, TASKS 4.6)** Driven through the executor and the real handlers: I1, I2, I3, I5, I6, I7, I8 and S33 as written in TASKS section 8 and SECURITY-REQUIREMENTS section 5 (claimant B gets 404 from the handler's ownership refusal). I4 (key format), the 403-before-400 order, S12, S34 (logs) and S35 need the HTTP pipeline and live in spec 3.
- **AC-8 (BR-84, ADR-009)** The Claims `DbContext` has no retrying execution strategy, and the executor is the only owner of `BEGIN`, `SAVEPOINT` and `COMMIT` for requests; handlers never begin or commit.
- **AC-9 (US-25, TASKS 4.1, 9)** On a Development start with an empty database the seeder creates 12 policies and about 30 claims across the six markets and the statuses Submitted, UnderReview, InfoRequested, PendingDecisionApproval, Approved and Rejected, each with a consistent trail and outbox rows, in one transaction. A second start seeds nothing. Outside Development nothing is seeded.
- **AC-9b (ARCHITECTURE s5, ADR-004)** Every command that writes outbox rows also updates the claim row under `xmin`; this is what keeps per-claim outbox order.
- **AC-10 (TASKS 4.1)** The schema has the tables, constraints and indexes of section 5; `Database.MigrateAsync()` up, down to zero and up again succeeds on an empty database.

## 3. Domain model and invariants
No new domain rules here; this spec stores and protects what spec 1 decides. Persistence-side invariants (each also a DB constraint, section 5): one open proposal per claim; one open information request per claim; checker differs from maker; Submitted if and only if unassigned; a Public trail entry never carries maker, checker, reason or a referenced entry (BR-06/BR-33 backstop); trail rows are never changed; an idempotency row exists only for a committed 2xx or committed stored refusal; every command that writes outbox rows also modifies the claim row.

What this spec needs from spec 1 was arbitrated in the spec 1 Review record, amendments **A1 to A5** (all accepted: reserve fields as `decimal?`, `Money` composed from `decimal` backing fields, two owned detail members, the `TrailEntry` binding constructor, `Claim.DrainPending()`); the companion rule of decision log 15 is already in spec 1. Decision log 16 applies: Phase 1 payable is capped per claim only; no cross-claim query, no policy-row lock here (Phase 2 note: aggregate remaining limit and the confirm-time `coverage-limit-exceeded` re-check arrive with Finance).

## 4. API (REST via Gateway)
No endpoints and no HTTP types (spec 3). This spec owns the **contract of `IdempotentCommandExecutor`** (ADR-009 steps 2 to 6) that the spec 3 filter adapts:

`ExecuteAsync(IdempotencyRequest request, Func<CancellationToken, Task<StoredResponse>> run, Func<DomainRefusal, StoredResponse> refusalToResponse, CancellationToken ct)` returns `Executed(StoredResponse)`, `Replayed(StoredResponse)`, `InProgress`, `KeyReused` or `NotStored(StoredResponse)`. `IdempotencyRequest(Guid ActorId, string Route, string Key, byte[] RequestHash)`; the HTTP adapter computes the canonical route and the hash (`RequestFingerprint.Compute(method, route, bodyBytes)` is a pure Application helper). `StoredResponse(int StatusCode, byte[] Body, string ContentType, string? Location)`. `run` performs validation and the handler and serialises the success into bytes; a non-2xx it returns (validation 400, 404) is `NotStored`.

| Concern | Rule |
|---|---|
| Scope | `(actor_id = token sub, route, key)`; `route` = `METHOD ` + the route template filled with the resolved values, GUIDs lower-case `D` format (built by the adapter, never the raw path) |
| Fingerprint | SHA-256 over method, `0x00`, canonical route, `0x00`, raw body bytes (BR-81, no canonicalisation) |
| Replay | same scope and fingerprint, unexpired: the stored response, flagged `Replayed` |
| Reuse | same scope, other fingerprint: `KeyReused`, nothing stored is returned |
| In flight | `pg_try_advisory_xact_lock` not obtained: `InProgress` (adapter: 409 `idempotency-request-in-progress`, `Retry-After: 1`); `run` never executes |
| Stored | 2xx from `run`, or a trailed `DomainRefusalException` whose Refused entry was written and mapped by `refusalToResponse`; nothing else (BR-86) |
| Not stored | non-2xx from `run`, an untrailed refusal (mapped through `refusalToResponse`, rolled back), any other exception (rolled back and rethrown) |
| Concurrency | different keys meet at `xmin`; the loser is a trailed refusal `claim-already-assigned` (pick-up) or `concurrency-conflict` (all others) |
Key format (8 to 128 chars of `A-Za-z0-9_-`; absent: `idempotency-key-missing`; malformed or several header values: `idempotency-key-invalid`), request buffering, the order authentication, role policy, binding, filter, and the problem results are spec 3.

## 5. Data
Database `claims_db`, schema `claims`, owned by role `claims` (already created by `docker/postgres/init-databases.sh`). Snake_case through explicit `ToTable`/`HasColumnName` (no naming package); history table `claims.__ef_migrations_history`. Money `numeric(18,2)` plus `char(3)` currency; timestamps `timestamptz` (UTC); enums stored as strings with CHECKs; GUID ids `uuid`.

### 5.1 Tables (all per ARCHITECTURE section 9 unless marked "added")
| Table | Constraints and indexes |
|---|---|
| `policies` | PK `id`; unique `policy_number`; CHECK market in the six; CHECK `(market, currency)` pair is one of SG/SGD, HK/HKD, MY/MYR, TH/THB, ID/IDR, AU/AUD; CHECK `coverage_limit >= 0`, `excess >= 0`, `valid_from <= valid_to` |
| `claims` | PK `id`; unique `claim_number` (regex CHECK `^CLM-[A-Z]{2}-[0-9]{4}-[0-9]{6}$`); FK `policy_id`; `xmin` row version; indexes `(status, market)`, `assigned_officer_id`, `submitted_at`, `(claimant_id, submitted_at)` (added, `GET /claims/mine`), CHECK status in the nine, type in (Motor, Property), same market/currency pairs, all amounts `>= 0`; CHECK `(status = 'Submitted') = (assigned_officer_id IS NULL)`; CHECK `status <> 'Rejected' OR reserve = 0` (added backstops). Phase 2 columns `paid`, `payee_on_file`, `payee_name` created nullable and unmapped by the domain |
| `claim_motor_details`, `claim_property_details` | PK = FK `claim_id` (owned types of `Claim`, own tables) |
| `claim_number_sequences` | PK `(market, year)`; CHECK `last_value BETWEEN 1 AND 999999` |
| `information_requests` | PK `id`; FK `claim_id`; unique `(claim_id) WHERE status = 'Open'`; CHECK status; CHECK `(status = 'Open') = (answered_at IS NULL)` |
| `proposals` | PK `id`; unique `(claim_id, id)` (added, target of the composite FK below); FK `claim_id`; unique `(claim_id) WHERE state = 'Open'`; CHECK `checker_id IS NULL OR checker_id <> maker_id`; CHECK kind, outcome, state; CHECK `outcome <> 'Approve' OR amount IS NOT NULL`; CHECK `outcome <> 'Reject' OR reason_code IS NOT NULL`; CHECK `(state = 'Open') = (resolved_at IS NULL)` |
| `claim_history` | PK `id`; unique `(claim_id, id)` (added); FK `claim_id`; composite FK `(claim_id, proposal_id)` to `proposals (claim_id, id)` and `(claim_id, referenced_entry_id)` to `claim_history (claim_id, id)` (added hardening: a link can never cross claims; MATCH SIMPLE, so null links are not checked); index `(claim_id, occurred_at, id)`; `reserve_before`/`reserve_after` `numeric(18,2)` null, staff-only, in the claim currency (BR-22), with the ARCHITECTURE s9 CHECKs: both `>= 0`; `reserve_before IS NULL OR reserve_after IS NOT NULL`; `reserve_after IS NULL OR reserve_before IS NOT NULL OR event_type = 'Submitted'` (the Public `Submitted` entry carries `reserve_after` = estimate and a null `reserve_before`); CHECKs: visibility, `actor_role` in (Claimant, Officer, Manager, System), `event_type` in the 14 types of spec 1 section 3.7, `summary <> ''`, from/to consistency (both null, or `to_status` set and `from_status` set unless `event_type = 'Submitted'`), `visibility <> 'Public' OR (maker_id, checker_id, reason, referenced_entry_id all NULL)`; reserve columns are stored exactly as the domain produced them (Submit, Assess, Confirm, Reassign auto-withdraw leaves them null) |
| `outbox` | PK `event_id`; `sequence bigint GENERATED ALWAYS AS IDENTITY` unique (ADR-010); columns `claim_id`, `type`, `version`, `envelope jsonb`, `occurred_at`, `published_at`, `attempts`, `last_error`; partial index `(sequence) WHERE published_at IS NULL` (ARCHITECTURE says `occurred_at`; ADR-010 orders by `sequence`, the architect updates the table row); CHECK `attempts >= 0`; CHECK `last_error IS NULL OR length(last_error) <= 1000` (O2: the text is sanitised before it is written, exception type plus message, no payload, no stack); no FK to claims (the publisher must not depend on claim rows) |
| `idempotency_keys` | PK `(actor_id, route, idempotency_key)`; index `expires_at`; CHECK `status_code BETWEEN 200 AND 499`; CHECK `length(idempotency_key) BETWEEN 8 AND 128`; no FK; no status column (ADR-009) |

### 5.2 EF mapping rules
- One `ClaimsDbContext` in `Infrastructure/Persistence`, one `IEntityTypeConfiguration<T>` per entity; `HasDefaultSchema("claims")`; `UseNpgsql` with **no** `EnableRetryOnFailure`; EF `EnableSensitiveDataLogging` never on; `Microsoft.EntityFrameworkCore.Database.Command` log level Warning outside tests.
- `claims`: `builder.Property<uint>("xmin").IsRowVersion()` (maps to the system column; the generated migration must not create or drop an `xmin` column, reviewer checks the scaffold and removes such lines by hand). Children (`proposals`, `information_requests`) carry no token: every Phase 1 command that touches a child also changes the claim row, so `xmin` on `claims` covers confirm against withdraw. `Correction` and `Refused` attempts must not touch the claim row (inserts into `claim_history` only, no `updated_at` bump, no `xmin` change), so they never cause or suffer a concurrency conflict; a test asserts the `xmin` is unchanged.
- Aggregate loaded for update with **every** `Proposal` of the claim (the aggregate itself decides "first decision proposal" from its own proposals; no extra query), `InformationRequests` and `Policy` (read-only, `AsNoTracking`-style: never saved); the trail is not loaded into the aggregate (spec 1: only `PendingEntries`).
- `TrailEntry` mapped to `claim_history` with the private constructor; `Claim.PendingEntries` and `PendingEvents` are `Ignore`d; enums with `HasConversion<string>()` and `HasMaxLength`.
- Raw SQL only through parameterised `FromSql`/`SqlQuery` interpolation (guidelines section 8); no string concatenation.

### 5.3 Migrations (reviewed, each with a working `Down`)
1. `InitialSchema`: policies, claims and details, claim_number_sequences, information_requests, proposals, claim_history (table, FKs, CHECKs, indexes; no trigger yet).
2. `ClaimHistoryAppendOnly` (hand-written SQL via `migrationBuilder.Sql`): in `Up` create `claims.block_history_change()` (PL/pgSQL, `RAISE EXCEPTION ... USING ERRCODE = 'restrict_violation'`), `BEFORE UPDATE OR DELETE ... FOR EACH ROW` trigger and `BEFORE TRUNCATE ... FOR EACH STATEMENT` trigger, then `DO $$ ... EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON claims.claim_history FROM %I', current_user) $$` (current role, not a hard-coded name, so compose, Testcontainers and production roles all work). `Down` runs first: the matching `GRANT` to `current_user`, `DROP TRIGGER`s, `DROP FUNCTION`. A statement-level TRUNCATE trigger also fires for `TRUNCATE ... CASCADE` reaching the table (PostgreSQL fires TRUNCATE triggers on every affected table).
3. `OutboxAndIdempotency`: `outbox` (with identity `sequence`) and `idempotency_keys`. **Privileges (O1):** the publisher needs SELECT and UPDATE on `outbox` (it sets `published_at`, `attempts`, `last_error`). In Phase 1 the `claims` role owns the table, so no GRANT is needed. Production note: the runtime role gets INSERT, SELECT and UPDATE on `outbox`, never DELETE or TRUNCATE (published rows are kept; a cleanup job is COULD).
Honest limit (ARCHITECTURE 3a): in Phase 1 the Claims role owns the table and runs migrations, so it could re-grant itself or drop the trigger. The guard stops application bugs, not a compromised container. Production: separate migration role, runtime role INSERT/SELECT only.
Startup: `Database.MigrateAsync()` runs in `Program` before the host accepts traffic (one replica, no race); `/health/ready` is therefore ready only after migration.

### 5.4 Claim number sequence
`IClaimRepository.NextClaimSequenceAsync(market, year)` runs, inside the submit transaction, `INSERT INTO claims.claim_number_sequences (market, year, last_value) VALUES ({market}, {year}, 1) ON CONFLICT (market, year) DO UPDATE SET last_value = claim_number_sequences.last_value + 1 RETURNING last_value` (parameterised, `Database.SqlQuery<int>`). The row lock is held to the end of the transaction, so numbers are serialised per market-year, never reused, and a rolled-back submit gives the number back (no gap). Year = UTC year of `context.Now`. Cost: submits of one market-year queue for the length of one short transaction; acceptable for the demo and stated in the walkthrough. A replay never reaches it (the filter answers first).

### 5.5 Seed data (Development only)
`ClaimsSeeder` (Infrastructure, hosted service registered only when `IHostEnvironment.IsDevelopment()`), runs after migration, **only when `policies` is empty**, inside **one** transaction (`ICommandTransaction`), so a crash half-way leaves nothing and the "policies empty" test stays valid.
- 12 policies: each seeded claimant (`DevSeed.SeedUserIds.Claimant`, `SecondClaimant`) x six markets, local-currency limit and excess scaled per currency (IDR values large), valid across 2026.
- About 30 claims from a fixed scenario table (deterministic, no `Random`): Motor and Property mix; roughly 6 Submitted, 6 UnderReview, 4 InfoRequested, 5 PendingDecisionApproval (at least one made by the dual-role `OfficerManager` to demonstrate S11, and one with a send-back history), 4 Approved, 5 Rejected; spread over all six markets and over submitted dates 1 to 40 days back (ageing demos).
- Every claim is created by calling the **real handlers** with a `CommandContext` per step: seeded actor and roles, `Now` stepping forward from the claim's chosen submitted date, a fresh correlation id per claim, `IIdGenerator`. So trail entries, entry counts, reserve rules and outbox rows are exactly what production produces; there is no `HasData` and no direct inserts of claims or trail rows. Only `policies` rows are inserted directly (no command exists; reference data).
- Outbox rows from seeding stay unpublished until the publisher slice exists, then flow to Kafka (Reporting and Notification start populated).

### 5.6 Identifier generation
Production `IIdGenerator` is `UuidV7IdGenerator` (Infrastructure): RFC 9562 UUIDv7 layout with a monotonic counter (method 1) under a lock, built with `new Guid(bytes, bigEndian: true)`. PostgreSQL `uuid` order then equals generation order, so the Internal primary entry always sorts before its Public companion (BR-12). GUIDs are never sorted in memory for display (.NET `Guid.CompareTo` is not PostgreSQL byte order); staff ordering is done in SQL. Not Npgsql's EF value generator (its monotonicity is not a contract). Tests use the sequential test generator from spec 1.

## 6. Events
Published: the 11 Phase 1 payload records of spec 1 section 3.10 / ADR-010, written as `outbox` rows, never to Kafka here.
- `IOutboxWriter.Add(Guid claimId, string market, IReadOnlyList<IClaimEventPayload> events, CommandContext context, Guid causationId)` wraps each payload in the `Contracts` `EventEnvelope`: `eventId` = `IIdGenerator` (UUIDv7, below), `type`/`version` from the record's constants, `occurredAt` = `context.Now`, `claimId`, `market`, `correlationId` = `context.CorrelationId`, `causationId` = id of the **primary** trail entry of the command (Internal when there are two), payload serialised with the Contracts JSON options; the envelope goes to `envelope jsonb`, which keeps neither key order nor bytes: the publisher sends `envelope::text` and consumers are tolerant readers (the ADR-003 golden-file test covers the serializer, not the stored column). Per-claim order holds because every command that writes outbox rows also updates the claim row under `xmin` (ARCHITECTURE section 5; test `Handlers_EveryCommandWithEvents_ModifiesClaimRow`). Written in `PendingEvents` order, so `sequence` equals raise order (test with Reassign + auto-withdraw).
- Written in the same `SaveChanges` as the claim and the trail entries. A refused attempt, correction or replay writes no row; a savepoint rollback discards the events of a refused command.
- **`IOutboxStore` (O3, Application interface, Infrastructure `PostgresOutboxStore`)** is the only seam the publisher (`messaging-engineer`) uses: `FetchUnpublishedBatchAsync(int max, ct)` returns unpublished rows ordered by `sequence` with `FOR UPDATE SKIP LOCKED` inside the publisher's own short transaction; `MarkPublishedAsync(eventId, publishedAt, ct)`; `RecordFailureAsync(eventId, sanitisedError, ct)` does `attempts + 1` and stores the sanitised text (at most 1,000 characters). The publisher itself is not in this slice.
- Tests that read `envelope` back from the database compare **parsed JSON** (e.g. `JsonNode.DeepEquals`), never text, because `jsonb` reorders keys and normalises whitespace (O5). The ADR-003 golden-file test also compares serializer output with the stored file as parsed JSON.
- Consumed: none.

## 7. Claim progress trail
Storage and write rules (the entry catalogue is spec 1 section 3.7, amended per section 3 item 1).
- One `SaveChanges` per command writes: the claim and children, all entries from `claim.DrainPending()`, the outbox rows. The idempotency row is added by the executor just before `COMMIT`.
- Entry counts the persistence tests assert (BA decision log 15, TASKS 4.2):

| Command | Trail rows | Outbox rows |
|---|---|---|
| Submit | +1 Public (`Submitted`) | `ClaimSubmitted` |
| PickUp | +1 Public | `ClaimAssigned` |
| Reassign | +1 Internal | `ClaimReassigned` (+ `ProposalWithdrawn` if auto) |
| Assess | +1 Internal | `ClaimAssessed` |
| RequestInformation, Answer | +1 Public | `InformationRequested`, `InformationProvided` |
| Propose, first on the claim | +2 (Internal + Public "Decision under review") | `DecisionProposed` |
| Propose, later (after send-back or withdrawal) | +1 Internal | `DecisionProposed` |
| Confirm | +2 (Internal + Public result) | `ClaimApproved` or `ClaimRejected` |
| SendBack | +1 Internal | `DecisionSentBack` |
| Withdraw | +1 Internal | `ProposalWithdrawn` |
| Correction | +1 Internal | none |
| Refused attempt | +1 Internal, `Refused` | none |
| Replay, 400, 401, role-policy 403, 404, 409 in-progress, 422 reused, 5xx | 0 | 0 |

- Refused entry: after the command throws a trailed `DomainRefusalException`, the executor does `ROLLBACK TO SAVEPOINT command`, `ChangeTracker.Clear()`, then `IRefusedAttemptRecorder.RecordAsync(exception, ct)` builds `TrailEntry.Refused(claimId, context, refusal)`, inserts it and `SaveChanges`; the executor stores the response produced by `refusalToResponse` and commits. The recorder reads who and when from `exception.Context` (so it never re-derives the actor).
- `DomainRefusalException(Guid ClaimId, CommandContext Context, DomainRefusal Refusal)` lives in Application. Handlers convert a failed `Result` in one place (`result.ThrowIfRefused(claimId, context)`). An untrailed refusal (422 domain validation, `claim-not-found`) reaches the executor the same way and is rolled back, not stored.
- Lost race: `IClaimUnitOfWork` catches `DbUpdateConcurrencyException` and throws `DomainRefusalException` with `DomainRefusals.ClaimAlreadyAssigned` (command PickUp) or `ConcurrencyConflict` (others); Application never sees the EF type.

## 8. Security
- Authorisation matrix: none added (spec 3). The executor sits behind authentication and the role policy (spec 3 pipeline order), so S3/S12 (403 with no record, no entry) and S35 (401 before lookup) are asserted there.
- Threats mitigated (SECURITY-REQUIREMENTS section 2 rows): trail tampering (REVOKE + triggers + immutable factory, S23); duplicate or replayed commands (ADR-009, I1 to I8, S32, S35); cross-actor replay (scope includes `actor_id` from the validated token, S33); stored-response leakage (executor logs only outcome and status code, never key, hash or body; reused returns nothing stored, no read endpoint over `idempotency_keys`, S34, spec 3); key flooding (stored only after authentication, role policy and a committed command; 64 KB Kestrel body limit; try-lock never waits). Known gap stated: no per-user rate limit, stored bodies not encrypted at rest.
- Never logged: the key, the request hash, request or response bodies, answer text, claim details. The store logs only route template, outcome (`Executed`, `Replayed`, `Reused`, `InProgress`) and status code, with `correlationId`. EF parameter values are never logged (sensitive logging off). Refusal `Detail` contains ids and codes only (spec 1).
- Actor id for the scope comes only from the validated token (BR-11), never from a header or body. Advisory-lock key and all SQL are parameterised.
- Secrets: connection string only from the environment (`ConnectionStrings__ClaimsDb`), no defaults in `appsettings*.json`; the compose default is dev-only and mirrors `.env.example`.
- Seeder never runs outside Development; seeded users are the dev ids from `Contracts.DevSeed`, no passwords involved.

## 9. Dependencies and sequencing
Needs: spec 1 (aggregate, `TrailEntry`, `DomainRefusal(s)`, `Result`, `CommandContext`, `IIdGenerator`) and its amendments in section 3 (can start schema and migrations as soon as spec 1's aggregate shape is reviewed, per the README); `Contracts` additions from ADR-010 (`EventEnvelope`, payload records, `HeaderNames.IdempotentReplayed`, `HeaderNames.RetryAfter` is the standard header, no constant); the existing Claims host with JWT validation. New packages in `Directory.Packages.props`: `Npgsql.EntityFrameworkCore.PostgreSQL` (8.x), `Microsoft.EntityFrameworkCore.Design` (design-time, private assets), tests: `Testcontainers.PostgreSql`, `Microsoft.Extensions.TimeProvider.Testing`; then lock files regenerated (Dockerfiles use locked-mode restore).
Unblocks: spec 3 (endpoints and the filter adapter reuse the handlers and the executor), messaging-engineer (outbox table and rows exist), Reporting/Notification (seeded events), frontend demo data.

### Compose entry for Claims (for `devops-engineer`; I do not edit compose)
- Service `claims`: `build` context `.`, dockerfile `src/Claims/Dockerfile` (exists); `<<: *hardening` (read-only, tmpfs `/tmp`, `cap_drop: ALL`, `no-new-privileges`); `healthcheck: *dotnet-healthcheck` (the host already serves `/health/ready`); `restart: unless-stopped`; networks `edge` and `data`; no published port (the Gateway already points at `http://claims:8080/`).
- `depends_on`: `jwt-keys` (`service_completed_successfully`) and `postgres` (`service_healthy`). Kafka and `kafka-init` are added with the publisher slice, not now.
- Volume: `jwt-public:/run/jwt-public:ro` only (no private key).
- Own environment list only (no `env_file`): `ASPNETCORE_ENVIRONMENT` (default Development, enables the seeder), `ConnectionStrings__ClaimsDb: Host=postgres;Port=5432;Database=claims_db;Username=claims;Password=${CLAIMS_DB_PASSWORD:-dev-only-claims-db}`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__PublicKeyPath`. `Settlement__Enabled: ${Settlement__Enabled:-false}` (true fails startup in Phase 1). Optional with defaults in code or `appsettings.json`: `Idempotency__Retention` (24 h), `Officers__KnownIds__0..2` (dev ids only, not secrets).
- Already present and unchanged: `claims_db`, role `claims` (owner of `claims_db`, `REVOKE ALL ... FROM PUBLIC`), `CLAIMS_DB_PASSWORD` on the `postgres` service, `.env.example` entry (devops to confirm it lists `CLAIMS_DB_PASSWORD`). The role must stay a non-superuser owner of its database (needed for the REVOKE to bite and for `CREATE SCHEMA claims`). No other service gets this password.
- Gateway: CORS must expose `Retry-After`, `Location`, `Idempotent-Replayed` and allow `Idempotency-Key` (identity-gateway spec; verify only).

## 10. Test plan
Integration tests in `tests/Claims.Tests/Persistence/` and `.../Idempotency/` with **Testcontainers PostgreSQL 16** (same major as compose). Fixture: one container, a **non-superuser login role** that owns a fresh database per test class (so `REVOKE` and `current_user` behave as in compose), migrations applied. `FakeTimeProvider` for expiry; sequential `IIdGenerator` except in the UUIDv7 test. **There is no HTTP pipeline and there are no test-only endpoints.** A small `CommandHarness` in the test project builds `IdempotencyRequest` values, calls `IdempotentCommandExecutor` directly with a `run` delegate that invokes the real handler and serialises the result, and a `refusalToResponse` delegate that maps a refusal to a fixed status and body. Races use a test-only `SaveChangesInterceptor` with a barrier (both executions have loaded the claim before either writes) or a gate (hold execution 1 in flight); forced failures use a trigger the test creates on `outbox` or `idempotency_keys` that raises and is dropped afterwards. No production seam is added for tests. I4, the 403-before-400 order, S12, S34 and S35 need HTTP and are in spec 3.

| Criterion / risk | Type | Test name |
|---|---|---|
| AC-1 BR-07 | integration | `Command_FailureAfterTrailInsert_LeavesNoClaimChangeTrailOutboxOrKey` |
| AC-1 BR-84, I7 | integration | `Command_FailureAfterOutboxInsert_LeavesNothingAndRetrySucceedsOnce` |
| AC-1 | integration | `Command_FailureAtIdempotencyInsert_RollsBackClaimTrailAndOutbox` |
| AC-1 | integration | `Command_Success_CommitsClaimTrailOutboxAndKeyTogether` |
| AC-2 BR-04 | integration (theory per command, all 12 handlers) | `Command_EachPhaseOneCommand_WritesExpectedTrailAndOutboxCounts` |
| AC-2 | integration | `ProposeDecision_FirstOnClaim_WritesInternalAndPublicEntry` |
| AC-2 | integration | `ProposeDecision_AfterSendBack_WritesInternalEntryOnly` |
| AC-2 | integration | `SendBackDecision_WritesInternalEntryOnlyAndNoPublicCompanion` |
| AC-2 | integration | `ConfirmDecision_WritesInternalAndPublicEntry` |
| AC-2 BR-37 | integration | `Reassign_AutoWithdraw_OutboxSequenceFollowsRaiseOrder` |
| AC-2 | integration | `ReserveColumns_SubmitAssessConfirm_StoredAsDomainProduced` |
| AC-3 S23 | integration | `ClaimHistory_UpdateDeleteTruncateAsOwner_FailWithPermissionDenied` |
| AC-3 S23 | integration | `ClaimHistory_AfterRegrant_TriggersBlockUpdateDeleteAndTruncate` (SQLSTATE 23001) |
| AC-3 | integration | `ClaimHistory_TruncateClaimsCascade_IsBlockedByTrigger` |
| AC-3 BR-06 | integration | `Correction_AddsRowAndLeavesOriginalEntryUnchanged` |
| AC-3 | integration | `ClaimHistory_PublicEntryWithMakerReasonOrReference_RejectedByCheckConstraint` |
| AC-3 | integration | `ClaimHistory_LinkToProposalOrEntryOfOtherClaim_RejectedByCompositeForeignKey` |
| AC-4 BR-26, S10 | integration | `PickUp_TwoOfficersAtOnce_OneWinsOtherGets409AndRefusedEntry` |
| AC-4 | integration | `PickUp_LoserAfterCommit_GetsInvalidTransitionAndRefusedEntry` |
| AC-5 BR-38, S15 | integration | `ConfirmDecision_TwoManagersAtOnce_OneWinsOtherGets409AndOneRefusedEntry` |
| AC-5 S15 | integration | `ConfirmAndWithdraw_AtOnce_OneWinner` |
| AC-5 | integration | `ConfirmDecision_Race_MoneyAppliedOnceAndOneApprovedEvent` |
| AC-6 BR-25 | integration | `Submit_TwentyConcurrent_NumbersUniqueAndContiguous` |
| AC-6 | integration | `Submit_RolledBack_DoesNotConsumeSequenceValue` |
| AC-7 I1 | integration | `Replay_SameKeyAndBytes_ReturnsStoredResponseMarkedReplayedAndWritesNothing` (sequence advanced once) |
| AC-7 I2 | integration | `Execute_SameKeyDifferentHash_ReturnsKeyReusedAndStateUnchanged` |
| AC-7 I3 | integration | `Execute_SameKeyInFlight_ReturnsInProgressThenReplays` |
| AC-7 I3 | integration | `Execute_SameKeyInFlight_WritesNoRefusedEntry` |
| AC-7 I5, S33 | integration | `Execute_OtherActorSameKey_RunsAsOwnRequestAndNeverReceivesStoredResponse` |
| AC-7 S33 | integration | `InfoResponse_OtherClaimantWithFirstClaimantsKey_HandlerReturnsClaimNotFound` |
| AC-7 I6 | integration | `Execute_ExpiredRecord_IsIgnoredAndRequestRunsAsNew` |
| AC-7 BR-87 | integration | `Lookup_ExpiredRecord_IsDeletedInSameTransaction` |
| AC-7 I7 | integration | `Execute_UnexpectedException_StoresNoRecordAndRethrows` |
| AC-7 I8 BR-86 | integration | `Confirm_MakerConfirmsOwnProposalTwice_TwoRefusalsOneRefusedEntrySecondReplayed` |
| AC-7 I8 | integration | `Execute_NonStoredResponseThenValidRunSameKey_SecondRuns` |
| AC-7 BR-86 | integration (theory: run returns 400, run returns 404, untrailed 422 refusal) | `Execute_NonStorableOutcome_StoresNoRecord` |
| AC-7 BR-84 | integration | `Execute_DifferentKeysSameClaim_BothStoredUnderOwnKey` |
| AC-8 | integration | `ClaimsDbContext_ExecutionStrategy_DoesNotRetry` |
| AC-8 | unit | `Handlers_NeverBeginOrCommitTransactions` (source scan of `Application/`) |
| AC-9 | integration | `Seeder_EmptyDatabase_CreatesTwelvePoliciesAndAboutThirtyClaimsInAllStatuses` |
| AC-9 O4 | integration | `Seeder_EachSeededClaim_HasTrailEntryCountsAndOutboxRowsMatchingItsHistory` (also asserts every seeded outbox row has `published_at IS NULL` and `attempts = 0`) |
| AC-9 | integration | `Seeder_SecondRun_SeedsNothing` |
| AC-9 | integration | `Seeder_Failure_LeavesNoPolicyOrClaim` |
| AC-9 | unit | `Seeder_NonDevelopmentEnvironment_IsNotRegistered` |
| AC-9b ARCH s5 | integration (theory over all handlers) | `Handlers_EveryCommandWithEvents_ModifiesClaimRow` (xmin differs after the command) |
| Handlers | integration | `Reassign_UnknownOfficer_ThrowsUnknownOfficerUntrailed` |
| Handlers | unit | `ConfigurationOfficerDirectory_KnownAndUnknownIds_ReturnsExpected` |
| Handlers | integration | `IssueCorrection_ReferencedEntryOfOtherClaim_EntryNotFoundAfterResourceRuleCheck` |
| Handlers | integration | `IssueCorrection_NonAssignedOfficerWithUnknownEntry_NotAssignedOfficerNotEntryNotFound` |
| Handlers | integration (theory 4 commands x statuses) | `SettlementStub_AnyStatus_Phase2NotEnabledRefusedAndStored` |
| Handlers | unit | `SettlementOptions_EnabledTrue_FailsStartupValidation` |
| AC-10 | integration | `Migrations_UpDownToZeroUp_Succeeds` |
| AC-10 | integration | `Model_AllTablesAndColumns_AreSnakeCase` (reflection over the EF model) |
| AC-10 | integration | `Schema_UniqueAndFilteredIndexes_RejectSecondOpenProposalAndSecondOpenInfoRequest` |
| AC-10 | integration | `Schema_CheckConstraints_RejectCheckerEqualsMakerBadStatusReserveColumnsAndMarketCurrencyPair` |
| AC-10 TASKS 4.1 | integration | `Schema_Indexes_ExistOnStatusMarketAssigneeSubmittedAtAndClaimantSubmittedAt` |
| Concurrency token | integration | `Claim_StaleUpdate_ThrowsConcurrencyAndMigrationHasNoXminColumn` |
| Correction/Refused no xmin bump | integration | `CorrectionAndRefusedAttempt_DoNotChangeClaimRowOrXmin` |
| Outbox shape O5 | integration | `OutboxRow_Written_HasEnvelopeFieldsCorrelationCausationAndUnpublishedState` (envelope compared as parsed JSON) |
| O3 | integration | `FetchUnpublishedBatch_ReturnsSequenceOrderAndSkipsPublished` |
| O3 | integration | `FetchUnpublishedBatch_ConcurrentCallers_GetDisjointRowsViaSkipLocked` |
| O3 | integration | `MarkPublished_SetsPublishedAtAndRowLeavesTheBatch` |
| O3 O2 | integration | `RecordFailure_IncrementsAttemptsAndStoresSanitisedErrorWithinLimit` |
| O2 | integration | `Schema_OutboxLastErrorOver1000Characters_RejectedByCheckConstraint` |
| Ids | unit | `UuidV7IdGenerator_TenThousandCalls_StrictlyIncreasingInPostgresOrder` |

## 11. Implementation plan
Each step is a commit (`feat:`, `test:`, `chore:`), tests in the same step. Estimate about 3 hours 25 minutes of the 5-hour cap, the largest Claims spec; do not start before the Review record shows REVIEWED.
1. Packages, `ClaimsDbContext`, configurations for policies/claims/details/sequences/children, migration 1, `Migrations_UpDownToZeroUp`, schema and constraint tests, Testcontainers fixture (35 min).
2. `claim_history` mapping, migration 2 (trigger, REVOKE, `Down`), S23 tests (20 min).
3. `IClaimRepository`, `IClaimUnitOfWork` (drain, concurrency translation), `IOutboxWriter`, migration 3 (outbox, idempotency), `DomainRefusalException` and `ThrowIfRefused`, claim number sequence, `UuidV7IdGenerator`, atomicity tests (35 min).
4. All twelve handlers with their command records, `IOfficerDirectory` and `ConfigurationOfficerDirectory`, `Settlement:Enabled` options, the xmin-on-every-event-command invariant test (35 min).
5. `IOutboxStore` and `PostgresOutboxStore` (with its four tests, 10 min), `ICommandTransaction`, `IIdempotencyStore`, `PostgresIdempotencyStore`, `IRefusedAttemptRecorder`, `RequestFingerprint`, `IdempotentCommandExecutor`, `Idempotency` options validated at startup (35 min).
6. `CommandHarness`, race interceptor, I1 to I3, I5 to I8, S33, race and entry-count tests (35 min).
7. `ClaimsSeeder` and scenario table, seeder tests (15 min).
8. Hand the compose list to `devops-engineer`; run the CODING-GUIDELINES section 15 checklist (build, format, test).
Folders: `Application/{Commands,Abstractions,Idempotency}`, `Infrastructure/{Persistence/{Configurations,Migrations},Idempotency,Outbox,Seeding,Identifiers}`. Configuration keys: `ConnectionStrings:ClaimsDb`, `Idempotency:Retention` (default `24:00:00`, positive), `Officers:KnownIds`, `Settlement:Enabled` (false; true fails startup). Cut line: nothing in AC-1 to AC-8 is cut (trail, refused entries, idempotency are never cut, ADR-009). If time runs short: reduce the seed to about 20 claims, drop the composite-FK hardening and the extra CHECK backstops (keep the ARCHITECTURE ones); delete no test of AC-1 to AC-7.

## 12. Risks, assumptions, open questions
Risks:
1. The executor-owned transaction plus `SaveChanges` in handlers is the delicate part: a nested commit or a retrying strategy would break atomicity. Mitigation: AC-8 tests, no `EnableRetryOnFailure`, the executor is the only caller of `ICommandTransaction` for requests (the seeder uses it once for its single transaction).
2. The transaction must run at Read Committed (the Npgsql default). Under Repeatable Read a snapshot taken before the advisory lock could miss a row committed by the first request; the executor never sets another isolation level.
3. The advisory-lock key is a 64-bit hash of `actor|route|key`; a collision would give a false `InProgress` (about 2^-64, accepted); the primary key stays the backstop for the inverse.
4. Outbox order across transactions: per-claim order depends on every event-writing command also updating the claim row under `xmin` (ARCHITECTURE section 5); the invariant is a test (AC-9b). Publisher-side checks are for `messaging-engineer`.
5. The stored refusal body contains the first response's correlation id; a replay returns it unchanged (BR-82 allows transport headers to differ, the body does not).
6. Tie order of two entries with equal `occurred_at` relies on UUIDv7 ids (section 5.6) and SQL ordering; never an in-memory GUID sort.
7. Unique violation or any exception inside `SaveChanges` aborts the PostgreSQL transaction; only the savepoint rollback or full rollback is valid afterwards. The executor never continues after an unexpected exception.

Assumptions: handlers return the `Claim` aggregate and spec 3 maps it to the claimant or staff view; `claim-not-found` is an untrailed `DomainRefusal` (rolled back, not stored); the test project `tests/Claims.Tests` references `src/Claims`, `tests/TestSupport` and Testcontainers only; the seeder runs the handlers directly (no executor, no key) inside one `ICommandTransaction`.

Open questions: none for the architect remain from the review (Q1 to Q8 answered in the Review record and applied above). For `messaging-engineer`: check points (a) to (f) in the Review record (publisher side only). Q10 (devops-engineer): confirm the Claims compose entry in section 9, including that `.env.example` lists `CLAIMS_DB_PASSWORD`.

## 13. Review record
| Reviewer | Date | Verdict (REVIEWED / CHANGES REQUESTED) | Notes |
|---|---|---|---|
| architect | 2026-10-28 | CHANGES REQUESTED | Strong spec. It fits ARCHITECTURE sections 3a, 5 and 9, ADR-004, ADR-005 (amended), ADR-009, ADR-010 and SECURITY-REQUIREMENTS: one atomic unit per command; filter-owned transaction with no retrying strategy; Read Committed; savepoint then Refused entry; `xmin` covers the children; correction and refusals never touch the claim row; REVOKE plus triggers with an honest limit; the `current_user` REVOKE; the sequence via `ON CONFLICT ... RETURNING`; the seeder runs through the real handlers in one transaction; entry and outbox counts follow decision log 15; the per-claim cap follows decision log 16. Change 1 fixes a real contradiction; changes 2 to 4 move work between specs so each spec can test itself. The rest is small. |

**Required changes (numbered).**
1. **Reserve CHECKs (5.1 `claim_history`) contradict spec 1 and ARCHITECTURE.** The Public `Submitted` entry carries `reserve_after` = estimate with `reserve_before` null. Remove `visibility <> 'Public' OR both NULL` and "both null or both >= 0". Use the ARCHITECTURE section 9 CHECKs instead: `reserve_before IS NULL OR reserve_after IS NOT NULL`; `reserve_after IS NULL OR reserve_before IS NOT NULL OR event_type = 'Submitted'`; both `>= 0`. Replace `Assess_WritesReserveBeforeAndAfterOnInternalEntryOnly` with `ReserveColumns_SubmitAssessConfirm_StoredAsDomainProduced` (spec 1 A1).
2. **Idempotency split (Q2, Q5; ADR-009 amendment).** This spec builds an HTTP-free `IdempotentCommandExecutor` (Application) that runs steps 2 to 6: lock, lookup or replay or reuse or expiry, savepoint, handler, storability, Refused entry, insert, commit. It takes the scope, the request hash and two delegates (run the handler; turn a result or refusal into a `StoredResponse`), plus `IIdempotencyStore`, `ICommandTransaction`, the recorder and the options. Move `IdempotencyFilter` (now a thin adapter), `RequestBufferingMiddleware`, the key-format validator and `ProblemResults` to spec 3. In section 4, keep the table as the executor's contract, except that the "Header" row moves to spec 3. The format rule changes there: several header values give `idempotency-key-invalid`, not `-missing`.
3. **Tests (Q2): no test-only endpoints.** Run I1 to I3, I5 to I8, S33 (cross-actor; claimant B gets 404 from the handler's ownership refusal), AC-1 atomicity, AC-4/AC-5 races and the non-storable outcomes (400 domain backstop, 404, untrailed 422, exception) against the executor and the real handlers on Testcontainers. Drive them with a small test harness that calls the executor directly; it is not an HTTP pipeline. Move these to spec 3: I4 (key format), `OfficerOnlyUserConfirmWithoutKey_Returns403Not400`, `RolePolicy403_LeavesNoIdempotencyRecordAndTrailUnchanged`, `Replay_ExpiredOrMissingToken_Returns401WithoutReplayHeader` (S35), the S34 log test and the `Microsoft.AspNetCore.Mvc.Testing` package. Remove "re-pointed by spec 3" from section 10 and from plan step 6.
4. **Handler ownership (Q1): every command handler is in this spec** (command side = spec 2; HTTP and read side = spec 3). Add Reassign (with `IOfficerDirectory`: Application interface, an Infrastructure implementation bound to the configured officer ids from `Contracts.DevSeed`, and `unknown-officer` raised in the handler), Correction (the handler checks that the referenced entry exists on the same claim through `IClaimRepository`, else `entry-not-found`; the composite FK is the backstop), and the settlement-stub refusal handler (reads `Settlement:Enabled`). Spec 3 keeps endpoints, DTO mapping, validators, policies, queries and `claims.yaml`. Update scope, out-of-scope, plan step 4 (about +15 min) and the section 7 table (Reassign and Correction are no longer "(spec 3)").
5. **Outbox order invariant (Q6, risk 4), ruled in ARCHITECTURE section 5.** Per-claim order holds because every command that writes outbox rows also updates the claim row under `xmin`. Make that an invariant with the test `Handlers_EveryCommandWithEvents_ModifiesClaimRow` (theory over all handlers). Turn risk 4 into a reference to ARCHITECTURE section 5 and close Q6's architect part.
6. **Indexes (Q4).** Keep the composite FKs, every extra CHECK (with change 1) and `(claimant_id, submitted_at)`. Drop `policyholder_id`, `policy_id` and `(policy_id, status)`: no Phase 1 query reads by them (lookup is by unique `policy_number`, and decision log 16 removed the remaining-limit sum); Phase 2 adds them. The cut line stays as written. ARCHITECTURE section 9 is updated.
7. **`IIdGenerator` (Q8).** Accepted with a change. Use an Infrastructure `UuidV7IdGenerator`: RFC 9562 layout with a monotonic counter (method 1) under a lock, built with `new Guid(bytes, bigEndian: true)` (.NET 8), so PostgreSQL `uuid` order equals generation order and the Internal primary always sorts before its Public companion (BR-12). Not Npgsql's EF value generator: its monotonicity is not a documented contract and it is not meant to be used outside EF. Never sort GUIDs in memory for display, because .NET `Guid.CompareTo` does not use PostgreSQL byte order; staff ordering is done in SQL. Test: `UuidV7IdGenerator_TenThousandCalls_StrictlyIncreasingInPostgresOrder`.
8. **Section 3:** replace the four asks with "arbitrated in spec 1 Review record A1 to A5" (all accepted; the companion rule was already applied in spec 1).

**Architect answers.**
- **Q1:** see change 4. Why not split handlers across specs: the transaction unit, the seeder and the executor tests all need them, and a split gives two owners for one layer.
- **Q2:** neither proposal; split by layer (changes 2 and 3). Why not test-only endpoints: they rebuild the middleware order that the tests are meant to check, and they are thrown away later. Why not move every idempotency test to spec 3: the core logic would stay untested until the last Claims spec.
- **Q3:** spec 1 A1 to A5, all accepted, with A1 (reserve fields as `decimal?`, `ReserveBefore` null only on Submitted) added.
- **Q4:** change 6.
- **Q5:** `ProblemResults` is owned by spec 3 (guidelines section 6: map errors in one place). The executor receives the mapping as a delegate, so this spec builds none.
- **Q6 (ruled by the architect; still for `messaging-engineer` to check):** accepted: the columns, the partial index on `sequence`, `causationId` = primary trail entry id, and `jsonb` with the Contracts JSON options. Note that `jsonb` does not keep key order or bytes, so the publisher sends `envelope::text` and consumers are tolerant readers; the ADR-003 golden-file test checks the serializer, not the stored column. The `messaging-engineer` should check: (a) no high-watermark, select unpublished `ORDER BY sequence`; (b) on a failed publish, stop the batch so no later row of the same claim goes first; (c) one publisher instance per database (otherwise `SKIP LOCKED` can split one claim's rows); (d) `acks=all`, `enable.idempotence=true`, and `published_at` set only after the ack; (e) the envelope field names against ARCHITECTURE section 7 and ADR-010; (f) `attempts`/`last_error` and when a row counts as poison (publisher side, not a consumer DLQ).
- **Q8:** change 7.
- **BA decision log 16:** reflected. ARCHITECTURE (confirm row, `claims` indexes) and SECURITY-REQUIREMENTS (per-claim cap listed as a Phase 1 control gap) now mark the aggregate limit, policy-row lock and `coverage-limit-exceeded` as Phase 2. |
| architect (re-review) | 2026-10-28 | CHANGES REQUESTED | Changes 1 to 8 are applied correctly. Reserve CHECKs now match spec 1 A1; the executor contract (section 4) is clean and HTTP-free; every handler is here; the order invariant is AC-9b with its test; the indexes are trimmed; UUIDv7 is in section 5.6; section 3 points to A1 to A5. The only open items are five from the messaging-engineer's outbox review, which I have ruled (ARCHITECTURE section 5, ADR-004 amendment). They are small and fully specified; re-review checks only O1 to O5. Plan steps 1, 2 and 4 to 7 are unaffected. |

**Required changes (re-review, outbox).**
- **O1 Privileges (5.3 migration 3).** State that the publisher needs SELECT and UPDATE on `outbox` (marking `published_at`, `attempts`, `last_error`). In Phase 1 the `claims` role owns the table, so no GRANT is needed; record that as the reason. Add the production note: the runtime role gets INSERT, SELECT and UPDATE on `outbox`, never DELETE or TRUNCATE (published rows are kept; a cleanup job is COULD).
- **O2 `last_error` limit (5.1 `outbox`).** Add CHECK `last_error IS NULL OR length(last_error) <= 1000`, matching ARCHITECTURE section 9. The text is sanitised before it is written: exception type plus message, no payload, no stack.
- **O3 `IOutboxStore` seam (Application interface, Infrastructure implementation, this spec).** `FetchUnpublishedBatchAsync(int max, ct)` returns rows ordered by `sequence`, using `FOR UPDATE SKIP LOCKED` inside the publisher's own short transaction. Also `MarkPublishedAsync(eventId, publishedAt, ct)` and `RecordFailureAsync(eventId, sanitisedError, ct)`, which does `attempts + 1`. The `messaging-engineer`'s publisher uses only this interface. Add one Testcontainers test per method, including `FetchUnpublishedBatch_ReturnsSequenceOrderAndSkipsPublished`.
- **O4 Seeded outbox rows stay unpublished.** Section 5.5 already says so; add the assertion `published_at IS NULL` and `attempts = 0` to `Seeder_EachSeededClaim_HasTrailEntryCountsAndOutboxRowsMatchingItsHistory`.
- **O5 JSON comparison.** Add to section 6: any test that reads `envelope` back from the database compares **parsed JSON** (for example `JsonNode.DeepEquals`), never text, because `jsonb` reorders keys and normalises whitespace. The ADR-003 golden-file test compares serializer output with the stored file, also as parsed JSON.
- **Tidy-ups (non-blocking):** drop the stale "(ARCHITECTURE says `occurred_at` ...; the architect updates the table row)" note in 5.1 (done) and the "amended per section 3 item 1" note in section 7. `Topics` and `KafkaHeaderNames` (ADR-004 amendment) belong to the publisher slice, not this spec. |
| architect (final) | 2026-10-28 | REVIEWED | Checked only O1 to O5 and the claims-api change 2, and all are in. O1: SELECT and UPDATE stated with the Phase 1 owner reason and the production grant. O2: `length(last_error) <= 1000` CHECK and sanitising. O3: `IOutboxStore` with its three methods and tests. O4: seeded rows asserted `published_at IS NULL` and `attempts = 0`. O5: parsed-JSON comparison (`JsonNode.DeepEquals`). The Submit handler takes the policy number through the unique `policy_number`, with one `policy-not-found` for unknown and foreign policies. Coding may start. |
