# SPEC: Claims domain layer  (service: Claims)

Spec 1 of the Claims specs (`docs/specs/README.md`). Pure Domain code under `src/Claims/Domain/`: no EF, no HTTP, no Kafka, no clock, no random ids. Persistence (spec 2) and endpoints (spec 3) build on it.

| Field | Value |
|---|---|
| Author | backend-developer |
| Status | IMPLEMENTED (core demo path only, see Build scope) |
| Phase | P1 build (Phase 2 settlement states and edges are modelled in the table, disabled) |
| Stories (US-xx) | US-01 (submit), US-06 (pick-up), US-26 (assessed amount), US-27 (propose), US-28 (confirm), US-29 (send back), US-18 (one immutable entry per action, Correction), US-22 (refused attempts), US-12 (reassign), US-03 and US-20 (claimant masking, entries and status), US-04/US-07 (info request and answer), US-31 (withdraw) |
| Business rules (BR-xx) | BR-01 to BR-12, BR-13 to BR-16, BR-17 to BR-19, BR-21, BR-22, BR-23, BR-25, BR-26, BR-27, BR-31 to BR-39, BR-74, BR-75, BR-76 (decision log 15 and 16 apply) |
| Tasks (docs/planning/TASKS.md) | 4.1 (entities, state machine, flag-gated settlement edges, claim number, assessed amount, reserve rules), 4.2 (trail, Correction, masking), 4.3 (maker-checker, reassign, guard rules), 8 (domain unit tests) |
| Related ADRs | ADR-005 (maker-checker in the domain), ADR-009 (refusal shape, stored refusals), ADR-004 (outbox, events written by Application), ADR-010 (domain raises `Contracts` event records) |

**Build scope (human decision, 1-hour backend budget).** Implemented: the full 9-state table with the Phase 2 gate (verdict enum `TransitionVerdict` instead of a `Result`, because the table has no command), `ClaimNumber`, `Money`, `Policy.NetPayableFor` (per-claim cap), `Claim.Submit`, `PickUp`, `Assess`, `ProposeDecision`, `ConfirmDecision`, `SendBackDecision`, `DrainPending` (A5), `DomainRefusals`, `TrailEntry` (A1 reserve rule, A4 private constructor, factory via `TrailEntryDraft`), `TrailEntry.Refused`, `ClaimantTimeline`, `ClaimantView`, and 7 event payload records. Deferred (not in code, no stubs): Withdraw, Reassign, information request/answer, Correction, settlement stub commands, the matching refusals, trail types, events (`ClaimReassigned`, `InformationRequested`, `InformationProvided`, `ProposalWithdrawn`) and `Proposal` state Withdrawn. `Money` throws on invalid scale or sign (the Application validates at the edge); the EF parameterless constructor, `Details` owned members wiring and `PendingEntries`/`PendingEvents` accessors are left to spec 2 (only `DrainPending` is exposed). Tests live in `tests/Claims.Domain.Tests`.

## 1. Purpose and scope
The Domain layer owns every business rule about a claim: which transitions exist, who may take them, how money (reserve, net payable) moves, what the trail records and which part of it a claimant may see. Handlers (spec 2/3) load an aggregate, call one method, and persist what the aggregate produced; they contain no rule. A refused command returns a `DomainRefusal`; the aggregate is left unchanged.

**In scope:** `ClaimStatus` and the transition table with the Phase 2 gate; `Claim` aggregate (submit, pick-up, reassign, assess, request/answer information, propose, confirm, send back, withdraw, correction, settlement stub refusal); `Policy` (read-only reference, net-payable calculation); `Proposal`; `InformationRequest`; `Money`; `ClaimNumber`; `Actor`/`CommandContext`; `TrailEntry` model, summaries and the claimant projections (timeline and status); `DomainRefusal` with problem types and the "trailed" flag; domain events as `Contracts` payload records (ADR-010); unit tests.

**Out of scope / not now:** EF mappings, `xmin`/row version handling and `DbUpdateConcurrencyException` translation (spec 2; the domain only offers the refusal factories); idempotency (spec 2/3); role policies, ownership 404 and claimant DTOs (spec 3: role policy is NOT part of the domain, section 3.9); `IOfficerDirectory` (Application; the domain only checks "new officer differs from current"); claim-number sequence (spec 2 supplies the number); the `Reminder due` entry of BR-16 (COULD); staff timeline "corrected by" flags (a query in spec 3); every Phase 2 behaviour beyond the table and the disabled-refusal (payable freeze for settlement, Settling, Settled, payee, payment, ledger); exposure/dashboard maths (Reporting).

## 2. Acceptance criteria
Domain-level versions of the story criteria; each maps to tests in section 10 (ids AC-n).
- **AC-1 (US-01, BR-23/25/75, BR-22)** Submit with a valid policy of the caller creates status Submitted, currency of the policy market, reserve = estimate, claim number `CLM-{MKT}-{YYYY}-{NNNNNN}`, and exactly one Public entry "Claim received: {claimNumber}", actor role Claimant, fromStatus null, `ReserveBefore` null and `ReserveAfter` = estimate.
- **AC-2 (US-01)** Invalid input (policy not the caller's, incident outside the policy period, incident date in the future beyond the UTC+14 tolerance, estimate not positive or more than 2 decimals, market not the policy's, currency guard) is refused and creates nothing.
- **AC-3 (US-06, BR-26)** Pick-up works only from Submitted: UnderReview, assigned to the actor, one Public "Claim picked up for review". Any other status is `invalid-transition`.
- **AC-4 (BR-01, TASKS 4.1)** All 81 (from, to) pairs behave as the table: 7 Phase 1 edges allowed; with `Settlement:Enabled=false` every pair touching a Phase 2 state returns `phase-2-not-enabled`; all others `invalid-transition`. With the flag on, 12 edges are allowed. Rejected and Settled have no outgoing edge (BR-03).
- **AC-5 (US-26, BR-19/21/22)** Assess in UnderReview by the assigned officer: assessed 8,000, excess 500 gives net payable 7,500 and reserve 7,500; assessed 400, excess 500 gives 0; payable never exceeds the coverage limit; payable is capped at the policy coverage limit of the claim (per claim only in Phase 1, decision log 16); one Internal entry with assessed, excess, payable and `ReserveBefore`/`ReserveAfter`; repeat assessment allowed while UnderReview.
- **AC-6 (US-27, BR-17/18/19/21/35/36)** Propose in UnderReview by the assigned officer: status PendingDecisionApproval, reserve/paid unchanged, an Internal proposal entry (outcome, amount, rationale, makerId, proposalId) and, only when the claim has had no earlier decision proposal, a Public "Decision under review" (first propose +2, any later proposal +1). Reject may be proposed without an assessed amount. 422 for unknown reject code, approve without assessed amount (`assessed-amount-required`), approve with payable 0, payable above estimate without justification.
- **AC-7 (US-28, BR-31/32/34/38/39)** Confirm by a different user id applies the proposal exactly: Approved (reserve = payable) or Rejected (reserve 0); Internal entry with makerId, checkerId, proposalId, `ReserveBefore`/`ReserveAfter`; Public final entry (confirm is always +2). Same user id (also dual-role) returns `maker-cannot-check` and changes nothing. A stale or already-resolved proposalId returns `proposal-not-open`.
- **AC-8 (US-29, BR-10/33/34/35)** Send back by a non-maker with a reason of at least 10 characters returns UnderReview, same assignee, reserve unchanged; exactly one Internal entry (+1) and no Public companion: the claimant keeps seeing the earlier "Decision under review" and is never told (decision log 15).
- **AC-9 (US-31, BR-36/38)** Withdraw only by the proposal's maker, reason at least 10 characters; back to UnderReview; one Internal entry. Non-maker gets `not-proposal-maker`.
- **AC-10 (BR-36)** While PendingDecisionApproval: assess, request information and propose return `proposal-pending`; no field changes.
- **AC-11 (US-12, BR-27/37)** Reassign: Manager on UnderReview, InfoRequested, PendingDecisionApproval; Officer only as current assignee; reason at least 10 characters; new officer differs from the current one (`same-officer`); one Internal entry; status unchanged except that an open proposal is auto-withdrawn (back to UnderReview) and recorded; Submitted, Approved, Rejected and Phase 2 statuses return `invalid-transition` (Phase 1 reassign statuses are exactly those three). The claim stays assigned.
- **AC-12 (US-07/04, BR-13 to BR-16)** Request information: assigned officer, UnderReview, non-empty question, due = request + 14 days, one open request; second request `open-info-request-exists`. Answer: the claim's claimant, InfoRequested only; back to UnderReview, same officer, Public "Information received", response text not copied into the trail.
- **AC-13 (US-18, BR-74)** Correction by the assigned officer or any Manager, any status including final, reason at least 10 characters, corrected statement non-empty: one Internal entry referencing the earlier entry id; status, reserve, payable and events unchanged and no claim field (`UpdatedAt` included) is touched. A non-assigned officer without Manager role gets `not-assigned-officer`.
- **AC-14 (US-18, BR-05/06)** A trail entry cannot be built without its mandatory fields; entries are immutable (no setters); Public entries never carry Internal entry types.
- **AC-15 (US-22, BR-04)** Every refusal carries `IsTrailed`; resource-rule refusals are trailed and can be turned into exactly one Internal Refused entry naming actor and attempt; validation 422s are not.
- **AC-16 (US-03, US-20, BR-08/09/33)** `ClaimantTimeline.Project` returns only Public entries, ordered by time then id, exposing only id, time and summary: never an Internal entry, count, gap, status pair, reserve fields, proposalId, makerId or checkerId. During a pending proposal and after a send-back the claimant sees only the single "Decision under review".
- **AC-17 (BR-01, TASKS 4.1)** Settlement stub commands return `phase-2-not-enabled` in every status, trailed.
- **AC-18 (BR-22, BR-35)** Reserve table of section 3.5 holds for every command.
- **AC-19 (BR-33, decision log 15 Q10)** `ClaimantView.StatusFor` shows PendingDecisionApproval as UnderReview and PendingSettlementApproval as Approved; all other statuses unchanged.
- **AC-20 (decision log 15 Q1)** Entry counts per command: first propose +2, confirm +2, every other successful command +1.
- **AC-21 (architect item 7)** Correction and refused attempts change no claim field, so they never bump the row version.

## 3. Domain model and invariants
Folder `src/Claims/Domain/` (one project folder, namespace `Chubb.Claims.Domain`), one public type per file. Nothing in Domain references EF, ASP.NET or logging. It references only `Chubb.Contracts` (Roles, Markets, event payload records).

### 3.1 States and transitions (BR-01, ADR-005)
`ClaimStatus`: Submitted, UnderReview, InfoRequested, PendingDecisionApproval, Approved, Rejected, PendingSettlementApproval, Settling, Settled (Phase 2 states: the last three; persisted as strings, never reordered).

`ClaimStateMachine` (static, immutable table) holds the 12 edges with a `Phase` flag; `Evaluate(from, to, settlementEnabled)` returns `Result`.

| From | To | Triggering command | Phase |
|---|---|---|---|
| Submitted | UnderReview | PickUp | 1 |
| UnderReview | InfoRequested | RequestInformation | 1 |
| InfoRequested | UnderReview | AnswerInformationRequest | 1 |
| UnderReview | PendingDecisionApproval | ProposeDecision | 1 |
| PendingDecisionApproval | Approved | ConfirmDecision (Approve) | 1 |
| PendingDecisionApproval | Rejected | ConfirmDecision (Reject) | 1 |
| PendingDecisionApproval | UnderReview | SendBackDecision, WithdrawDecision, auto-withdraw on Reassign | 1 |
| Approved | PendingSettlementApproval | RequestSettlement | 2 |
| PendingSettlementApproval | Settling | ConfirmSettlement | 2 |
| PendingSettlementApproval | Approved | SendBackSettlement, WithdrawSettlement | 2 |
| Settling | Settled | PaymentCompleted (actor System) | 2 |
| Settling | Approved | CancelPayment | 2 |

Evaluation order: (1) if `settlementEnabled` is false and either state is Phase 2, return `phase-2-not-enabled` (409). (2) else if the pair is not an edge, return `invalid-transition` (409, detail names both states). (3) else success. Why Phase 2 first: a settlement attempt on a claim in the wrong state must still say "not built" (BR-04 lists the stubs as `phase-2-not-enabled`, TASKS 4.1). Self pairs are never edges. Counts for the test: flag off = 7 allowed, 45 `phase-2-not-enabled`, 29 `invalid-transition`; flag on = 12 allowed, 69 `invalid-transition`.

`Settlement:Enabled` is read by the Api/Application layer (options, spec 3). The Claim never reads configuration. Phase 1 settlement stubs call `Claim.RefuseSettlementCommand(context, SettlementCommand)` which delegates to `ClaimStateMachine.Evaluate(Status, PendingSettlementApproval, settlementEnabled: false)`-equivalent behaviour and returns the trailed refusal; when Phase 2 is built these methods are replaced by the real ones. No settlement behaviour beyond refusal is written now.

### 3.2 Claim aggregate
`Claim` (sealed, private setters, private parameterless constructor for EF in spec 2, no public constructor). Fields (all private set):

| Field | Type | Rule |
|---|---|---|
| `Id` | Guid | from `IIdGenerator` |
| `Number` | `ClaimNumber` | `CLM-{MKT}-{YYYY}-{NNNNNN}` (BR-25), market equals policy market; unique/never reused is the sequence (spec 2) |
| `PolicyId`, `Policy` | Guid, `Policy` | reference data, loaded by the handler; `Policy` is read-only |
| `ClaimantId` | Guid | the submitting actor's user id |
| `Details` | `ClaimDetails` | `MotorDetails(VehicleRegistration, IncidentLocation)` or `PropertyDetails(PropertyAddress, DamageType)`; the type is derived from it, so type and details cannot disagree |
| `Market`, `Currency` | string, string | from the policy, fixed forever (BR-23) |
| `IncidentDate`, `Description` | DateOnly, string | |
| `Estimated` | `Money` | > 0 |
| `Assessed`, `NetPayable` | `Money?` | null until the first assessment (BR-21) |
| `Reserve` | `Money` | section 3.5 |
| `Payable` | `Money?` | null until confirmed approval, then frozen to the proposal amount and never recalculated; a Phase 1 column, published in `ClaimApproved` |
| `Status` | `ClaimStatus` | |
| `AssignedOfficerId` | Guid? | null only while Submitted |
| `SubmittedAt`, `UpdatedAt` | DateTimeOffset | UTC from `context.Now`; `UpdatedAt` changes only on commands that change the claim (never on Correction or a refusal) |
| `InformationRequests`, `Proposals` | read-only lists | children |
| `PendingEntries`, `PendingEvents` | read-only lists | produced by the last command, drained by the handler |

Invariants (checked in tests, enforced by construction): Submitted <=> no assignee; every other status has an assignee; Currency never changes and equals the market currency; Reserve >= 0 and in the claim currency; at most one open proposal and it exists <=> status PendingDecisionApproval (Phase 1); at most one open information request and it exists <=> status InfoRequested; Approved implies Payable > 0 and Reserve = Payable; Rejected implies Reserve = 0; Rejected and Settled accept no command except Correction (BR-03); every successful command appends at least one entry and its state/trail/events change as one unit (the persistence transaction itself is spec 2).

### 3.3 Command surface (signatures)
All methods return `Result` or `Result<T>` (no exceptions for expected failures, guidelines section 6) and are no-ops on failure (checks first, mutation after). `CommandContext(Actor Actor, DateTimeOffset Now, Guid CorrelationId, IIdGenerator Ids)` keeps parameter counts at three or fewer.

```csharp
public sealed record Actor(Guid UserId, IReadOnlySet<string> Roles)   // built by Application from the token (BR-11)
{ public bool IsOfficer; public bool IsManager; public bool IsClaimant; public static Actor System(Guid serviceId); }

public static Result<Claim> Claim.Submit(CommandContext context, NewClaim request, Policy policy);   // ClaimNumber is in NewClaim
public Result PickUp(CommandContext context);
public Result Reassign(CommandContext context, Guid newOfficerId, string reason);
public Result Assess(CommandContext context, Money assessed, string note);
public Result RequestInformation(CommandContext context, string question);
public Result AnswerInformationRequest(CommandContext context, string response);
public Result<Guid> ProposeDecision(CommandContext context, DecisionProposal proposal);  // returns proposalId
public Result ConfirmDecision(CommandContext context, Guid proposalId);
public Result SendBackDecision(CommandContext context, Guid proposalId, string reason);
public Result WithdrawDecision(CommandContext context, Guid proposalId, string reason);
public Result IssueCorrection(CommandContext context, CorrectionRequest request);
public Result RefuseSettlementCommand(CommandContext context, SettlementCommand command);
```
`NewClaim` (claimant-settable fields plus the number from the sequence), `DecisionProposal(Outcome, ReasonCode?, Rationale, Justification?)`, `CorrectionRequest(ReferencedEntryId, Reason, CorrectedStatement)`. Amount is never in `DecisionProposal` or confirm: it is derived (BR-34, ARCHITECTURE section 9).

### 3.4 Rules per command
Check order for every command: (a) Phase 2 gate where relevant, (b) resource rule about the actor (assigned officer, maker, claimant), (c) state/proposal rule, (d) payload rules. Why: (a) to (c) are trailed refusals, (d) are untrailed validation, so inside the domain a refusal on a wrong state or wrong actor is never hidden behind a payload error and never leaks payload detail. Edge validation (FluentValidation 400, for example a missing or short reason) runs before the handler, so those 400s come before the trailed checks and are not trailed (BR-27, BR-79). Roles are never checked here except where the role changes the rule (reassign, correction, section 3.9).

| Command | Resource rule (403/404) | State rule (409) | Payload rules (not trailed) | Effect |
|---|---|---|---|---|
| Submit | policy.PolicyholderId = actor id, else `policy-not-found` 422 (also for another person's policy, no existence leak); incident date inside the policy period (`incident-outside-policy-period`) and not in the future (`incident-date-in-future`: `IncidentDate > DateOnly.FromDateTime(context.Now.UtcDateTime.AddHours(14))`, so a claimant ahead of UTC, for example AU at UTC+11, is never refused for today) | n/a | estimate > 0 and <= 2 decimals (the request has no currency: Application builds the estimate `Money` in the policy currency, so `currency-mismatch` is only a domain guard, not reachable through the API); market = policy market (`market-mismatch`, checked after the ownership rule); description not blank; details present | Submitted, reserve = estimate, 1 Public entry (`ReserveBefore` null, `ReserveAfter` = estimate), `ClaimSubmitted` |
| PickUp | none (any officer) | status Submitted (`invalid-transition` otherwise) | none | UnderReview, assignee = actor, Public entry, `ClaimAssigned`. Two officers at once is persistence (xmin); the loser reloads and gets `invalid-transition`, or `claim-already-assigned` from the conflict factory (BR-26) |
| Reassign | Officer actor must be `AssignedOfficerId` (`not-assigned-officer`); a Manager (including dual-role) may reassign any | UnderReview, InfoRequested, PendingDecisionApproval only (Phase 2 may add Approved and PendingSettlementApproval, never Settling); else `invalid-transition` | reason >= 10 chars (400 `validation-error` at the edge, domain backstop); new officer differs from current (`same-officer` 422, not trailed; `unknown-officer` stays for ids missing from `IOfficerDirectory`, checked by Application) | Assignee changes, status unchanged; an open proposal is auto-withdrawn and status returns to UnderReview (BR-37); one Internal entry; `ClaimReassigned` (+ `ProposalWithdrawn` if auto) |
| Assess | actor is the assigned officer | UnderReview; PendingDecisionApproval gives `proposal-pending`; other `invalid-transition` | assessed > 0, <= 2 decimals, claim currency; note <= length cap | `Assessed`, `NetPayable = policy.NetPayableFor(assessed)`, Reserve = NetPayable; one Internal entry (assessed, excess, payable) with `ReserveBefore`/`ReserveAfter`; `ClaimAssessed` |
| RequestInformation | assigned officer | UnderReview; InfoRequested gives `open-info-request-exists`; PendingDecisionApproval gives `proposal-pending`; other `invalid-transition` | question not blank | new open `InformationRequest`, due = now + 14 days, InfoRequested, Public entry carrying the question; `InformationRequested` |
| AnswerInformationRequest | actor id = `ClaimantId`, else `claim-not-found` 404 (defence in depth; Application already answers 404) | InfoRequested | response not blank | request answered, UnderReview, same officer, Public "Information received" (response text stays on the request, never in the trail, BR-81 PII); `InformationProvided` |
| ProposeDecision | actor is the assigned officer (= maker) | UnderReview; PendingDecisionApproval gives `proposal-pending`; other `invalid-transition` | rationale >= 10 chars; Reject: reason code in the six BR-17 codes (`reason-code-invalid`); Approve: assessed exists (`assessed-amount-required`), NetPayable > 0 (`payable-zero`), NetPayable <= Estimated or justification present (`justification-required`) | `Proposal` Open with amount = NetPayable for Approve, none for Reject; PendingDecisionApproval; reserve unchanged (BR-35); Internal entry, plus the Public "Decision under review" only if no earlier Decision proposal exists in `Proposals` (first propose +2, later +1); `DecisionProposed` |
| ConfirmDecision | actor id != proposal maker id (`maker-cannot-check` 403) | open proposal with this id exists, else `proposal-not-open` 409 (stale id, already resolved, wrong state) | none (no payload) | Approve: Approved, Payable = amount, Reserve = Payable; Reject: Rejected, Reserve = 0; proposal Confirmed with checker; Internal entry (with `ReserveBefore`/`ReserveAfter`) + Public final entry (+2); `ClaimApproved` or `ClaimRejected` |
| SendBackDecision | actor id != maker (`maker-cannot-check`) | as Confirm | reason >= 10 chars | UnderReview, same assignee, reserve unchanged, proposal SentBack; Internal entry only (+1), no Public companion; `DecisionSentBack` |
| WithdrawDecision | actor id = maker (`not-proposal-maker` 403) | as Confirm | reason >= 10 chars | UnderReview, proposal Withdrawn; Internal entry only; `ProposalWithdrawn` |
| IssueCorrection | Manager (any), or officer who is `AssignedOfficerId`; else `not-assigned-officer` | none: any status | reason >= 10 chars; corrected statement not blank; referenced id not empty | one Internal Correction entry; sets no claim field (`UpdatedAt` included), so it never bumps the row version or makes a concurrent confirm or pick-up lose; no event (ADR-010) |
| RefuseSettlementCommand | none | always `phase-2-not-enabled` | none | no change; trailed refusal |

Refused attempts likewise touch no claim field: `TrailEntry.Refused` is built from the claim id and the refusal alone (3.7).

For Confirm/SendBack/Withdraw the proposal check precedes the maker check, so a stale id is `proposal-not-open` even for the maker, and a dual-role maker confirming their own open proposal gets `maker-cannot-check` (S11). The former maker may check a later proposal made by a different officer (BR-37). Maker and checker are compared on `UserId` only; `Roles` never enter the comparison.

### 3.5 Money and reserve (BR-19, BR-22, BR-35)
`Money` is a `readonly record struct (decimal Amount, string Currency)`: `Create` rejects more than 2 decimals (no silent rounding), negatives where not allowed and non ISO-shaped currency; arithmetic between different currencies throws `InvalidOperationException` (a programmer error: the command boundary already returned `currency-mismatch`). Never `double`.

`Policy.NetPayableFor(assessed)` = `min(max(assessed - Excess, 0), CoverageLimit)`. Per decision log 16 the Phase 1 cap is the policy coverage limit per claim only: it is static, so there is no headroom input, no query across claims and no confirm-time re-check. The seed must not contain a payable above its policy limit. Phase 2 note (documented, not built): with Finance the cap becomes the aggregate remaining limit, re-checked at confirm with refusal `coverage-limit-exceeded` (409, trailed, proposal stays pending, checker sends back), and confirms on one policy are serialised. TASKS 4.1 states the formula without the cap; BR-19 wins (its owner aligns it).

| Event | Reserve after | Other money fields |
|---|---|---|
| Submit | = Estimated | none |
| Assess | = NetPayable of the assessment (may rise or fall) | Assessed, NetPayable set |
| Propose, SendBack, Withdraw, auto-withdraw | unchanged | unchanged |
| Confirm Approve | = proposal amount = Payable | Payable frozen |
| Confirm Reject | 0 | none |
| Reassign, Info request/answer, Correction, PickUp | unchanged | unchanged |
Reserve before/after are the `ReserveBefore`/`ReserveAfter` fields of the causing command's primary entry (staff only) and are in the event payload; there is no separate "Reserve adjusted" entry (decision log 15 Q2). Exposure itself is computed by Reporting from events.

### 3.6 Proposal and InformationRequest
`Proposal` (child entity, all mutation internal to the aggregate): `Id`, `Kind` (`Decision`; `Settlement` reserved and not constructible in Phase 1), `Outcome` (Approve/Reject), `Amount` (Money?), `ReasonCode` (enum of six, BR-17), `Rationale`, `Justification?`, `MakerId`, `CheckerId?`, `State` (Open, Confirmed, SentBack, Withdrawn), `ResolutionReason?`, `ProposedAt`, `ResolvedAt?`. Invariants: `CheckerId` set only for Confirmed/SentBack and never equals `MakerId` (mirror of the DB CHECK); a resolved proposal is never reopened; amount present for Approve and equal to the claim's NetPayable at proposal time; Reject has a reason code.

`InformationRequest`: `Id`, `Question`, `Response?` (personal data), `Status` (Open, Answered), `RequestedBy`, `RequestedAt`, `DueAt` (= RequestedAt + `InformationRequestRules.ResponseWindow`, 14 days, BR-16), `AnsweredAt?`.

### 3.7 Trail model (BR-04 to BR-12, BR-33, BR-74)
`TrailEntry` (immutable sealed record, factory-only construction): `Id, ClaimId, OccurredAt, EventType, FromStatus?, ToStatus?, ActorId, ActorRole, Summary, Reason?, Visibility, CorrelationId, ProposalId?, MakerId?, CheckerId?, ReferencedEntryId?, ReserveBefore?, ReserveAfter?` (`Money?` in the claim currency, ARCHITECTURE section 9 columns `reserve_before`, `reserve_after`; both or neither; set on the primary entry of Submit (null to estimate, on the Public Submitted entry), Assess, and Confirm (approve: to payable; reject: to 0); allowed on a Public entry but never projected). The factory throws `ArgumentException` for a missing mandatory field (BR-05: empty ids, blank summary, unknown role, from/to inconsistent: FromStatus null only for Submitted, both set whenever the status changes, both null otherwise). That is a programmer error, not a user refusal.

`TrailEntryType` fixes visibility per type, so a type can never be both Public and Internal:

| Type | Visibility | Written by | Status change on the entry |
|---|---|---|---|
| Submitted | Public | Submit | null -> Submitted |
| Assigned | Public | PickUp | Submitted -> UnderReview |
| Reassigned | Internal | Reassign | UnderReview/InfoRequested unchanged; Pending -> UnderReview when auto-withdrawn |
| InformationRequested | Public | RequestInformation | UnderReview -> InfoRequested |
| InformationProvided | Public | AnswerInformationRequest | InfoRequested -> UnderReview |
| Assessed | Internal | Assess | none |
| DecisionProposed | Internal | ProposeDecision | UnderReview -> PendingDecisionApproval |
| DecisionUnderReview | Public | first ProposeDecision only (companion) | none (carried by the Internal twin) |
| DecisionConfirmed | Internal | ConfirmDecision | Pending -> Approved/Rejected |
| DecisionAnnounced | Public | ConfirmDecision (companion) | none |
| DecisionSentBack | Internal | SendBackDecision | Pending -> UnderReview |
| ProposalWithdrawn | Internal | WithdrawDecision | Pending -> UnderReview |
| Correction | Internal | IssueCorrection | none; `ReferencedEntryId` set |
| Refused | Internal | `TrailEntry.Refused(...)` | none |

Rule (decision log 15 Q1): one primary entry per action plus at most one Public companion where BR-08 defines a claimant milestone. Propose, confirm and send back write an Internal entry carrying `ProposalId`, `MakerId`, `CheckerId` and the status change. The Public companion exists only for (a) the claim's first decision proposal ("Decision under review") and (b) confirm (the final result); it has the same `OccurredAt` and `ProposalId`, no status fields, no names and a neutral summary. The aggregate decides "first" from its own `Proposals` (any earlier proposal of kind Decision, whatever its state), so spec 2 must load every proposal of the claim, not only the open one. Send-back, withdrawal and later proposals are Internal only. Counts: first propose +2, confirm +2, all else +1. Ordering inside one command is Internal first.

`TrailSummaries` (static pure functions, one per type, unit tested): "Claim received: CLM-SG-2026-000123", "Claim picked up for review", "Information requested: {question}", "Information received", "Decision under review", "Claim approved for SGD 7,500.00", "Claim rejected: {code label}, {rationale}", plus Internal ones such as "Assessed amount set to 8,000.00 (excess 500.00, payable 7,500.00, reserve 10,000.00 to 7,500.00)", "Decision proposed: approve 7,500.00 SGD", "Refused: ConfirmDecision (maker-cannot-check)". Summaries carry money, codes and the claimant-facing texts of BR-08 only: no bank data, no response text, no user names (ids are on the entry fields), and Public summaries contain no outcome while a proposal is pending.

`TrailEntry.Refused(Guid claimId, CommandContext context, DomainRefusal refusal)` builds the Internal Refused entry from a refusal that `IsTrailed`; it throws if not trailed. `Actor` role on it is the best acting role (Officer/Manager/Claimant). It is a static factory so the `IRefusedAttemptRecorder` (spec 2) can create it after the savepoint rollback without needing a mutated aggregate; it sets no claim field, so a refusal never bumps the row version.

`ClaimantTimeline.Project(IEnumerable<TrailEntry>) : IReadOnlyList<ClaimantTimelineItem>` (pure, static): keep `Visibility == Public`, order by `OccurredAt` then `Id` (BR-12), map to `(Id, OccurredAt, Summary)` only. No type, status, reserve fields, proposalId, maker/checker, correction, count or index. Defence in depth: if a Public entry has `MakerId`/`CheckerId`/`Reason`/`ReferencedEntryId`, `TrailEntry` construction already refused it, so the projection cannot leak by data drift.

`ClaimantView.StatusFor(ClaimStatus) : ClaimStatus` (pure, static, next to `ClaimantTimeline`): PendingDecisionApproval maps to UnderReview, PendingSettlementApproval maps to Approved (ready for Phase 2), all others unchanged. Spec 3 uses it for the claimant detail and list DTOs, so a send-back never shows as a visible status flip (BR-33).

### 3.8 DomainRefusal and problem types (ADR-009, BR-04, BR-86)
`DomainRefusal` (sealed record): `ClaimCommand Command` (which attempt, e.g. ConfirmDecision), `string Type` (bare problem type code, matching the Gateway convention), `int Status`, `string Detail` (no personal data), `bool IsTrailed`. `DomainRefusals` is the single factory class with named constants for every type. `Result` / `Result<T>` carry success or one refusal. Application throws `DomainRefusalException(refusal)` where ADR-009 needs the filter to catch it (spec 2); it lives in Application, not Domain (architect confirmed; the same type wraps the lost-race translation). Domain code never throws it.

| Type | Status | Trailed (resource rule) | Raised by |
|---|---|---|---|
| `invalid-transition` | 409 | yes | state rule of PickUp, Reassign, Assess, RequestInformation, Answer, Propose |
| `phase-2-not-enabled` | 409 | yes | state machine gate, settlement stubs |
| `proposal-pending` | 409 | yes | Assess, RequestInformation, Propose |
| `open-info-request-exists` | 409 | yes | RequestInformation |
| `proposal-not-open` | 409 | yes | Confirm, SendBack, Withdraw |
| `not-assigned-officer` | 403 | yes | Reassign, Assess, RequestInformation, Propose, IssueCorrection |
| `maker-cannot-check` | 403 | yes | Confirm, SendBack |
| `not-proposal-maker` | 403 | yes | Withdraw |
| `claim-already-assigned`, `concurrency-conflict` | 409 | yes | factories only; Application raises them on `DbUpdateConcurrencyException` (lost race, BR-26/BR-38) |
| `claim-not-found` | 404 | no (security event) | Answer when the actor is not the claimant (defence in depth) |
| `policy-not-found`, `market-mismatch`, `incident-outside-policy-period`, `incident-date-in-future` | 422 | no | Submit |
| `currency-mismatch` | 422 | no | domain guard on Submit and Assess; not reachable through the API (request has no currency), documented in `claims.yaml` |
| `reason-code-invalid`, `assessed-amount-required`, `payable-zero`, `justification-required` | 422 | no | Propose |
| `same-officer` | 422 | no | Reassign to the current assignee |
| `unknown-officer` | 422 | no | Application, id not in `IOfficerDirectory` |
| `entry-not-found` | 422 | no | factory only; Application checks the referenced entry (the aggregate does not hold the trail) |
| `validation-error` | 400 | no | backstop for payload rules (short or missing reason, blank text, bad scale); the edge validates first (FluentValidation); a missing or short reason is 400 |
| `coverage-limit-exceeded` | 409 | yes | Phase 2 only, not built (aggregate limit at confirm) |

Trailed means: the refusal is a resource-rule refusal after the role policy passed (BR-04, human decision 14). Role-policy 403s and ownership 404s are not in the domain at all.

### 3.9 Roles and policy boundary
The domain never decides "may this role call this endpoint" (that is the Api policy, default deny). It does use roles where the rule itself depends on them: Reassign and IssueCorrection (Manager may act on any claim, Officer only on their own), and the trail `ActorRole`. For a dual-role user, `ActorRole` on an entry is chosen by the command: Officer for assess/propose/withdraw/request information, Manager for confirm/send back, Manager when the actor is a Manager for reassign and correction otherwise Officer, Claimant for submit/answer. `ConfirmDecision` does not check that the actor holds Manager; the Api policy does; the domain checks only maker != checker.

### 3.10 Domain events (outbox, ADR-004)
Each successful command appends payload records to `Claim.PendingEvents`. They are `Contracts` types in `Chubb.Contracts.Events` (ADR-010). Conventions: records implement `IClaimEventPayload` with `const string EventType` and `const int Version = 1`; primitives only (Guid, string, decimal, int, DateOnly, DateTimeOffset, enums as strings; money as `amount` plus `currency`, never `Money`); no free text; every payload carries `status` (claim status after the command); `ClaimSubmitted`, `ClaimAssigned`, `InformationRequested`, `InformationProvided`, `ClaimApproved` and `ClaimRejected` also carry `claimantId`. The list order of `PendingEvents` is the publish order. The Application outbox writer adds the envelope (`eventId`, `occurredAt`, `claimId`, `market`, `correlationId`, `causationId`, key = claimId) in the same transaction; the Domain publishes nothing and knows no Kafka.

| Event (payload record) | Raised by | Payload fields |
|---|---|---|
| `ClaimSubmitted` | Submit | status, claimantId, claimNumber, claimType, currency, estimatedAmount, reserve |
| `ClaimAssigned` | PickUp | status, claimantId, officerId |
| `ClaimReassigned` | Reassign | status, fromOfficerId, toOfficerId |
| `InformationRequested` | RequestInformation | status, claimantId, requestId, dueAt |
| `InformationProvided` | Answer | status, claimantId, requestId |
| `ClaimAssessed` | Assess | status, assessedAmount, excess, netPayable, reserveBefore, reserveAfter, currency |
| `DecisionProposed` | Propose | status, proposalId, makerId, outcome, amount?, currency |
| `DecisionSentBack` | SendBack | status, proposalId, makerId, checkerId |
| `ProposalWithdrawn` | Withdraw, auto-withdraw on Reassign | status, proposalId, makerId, cause (ByMaker, OnReassignment) |
| `ClaimApproved` | Confirm Approve | status, claimantId, proposalId, makerId, checkerId, payable, currency |
| `ClaimRejected` | Confirm Reject | status, claimantId, proposalId, makerId, checkerId, reasonCode, reserveReleased, currency |
Free text (question, response, rationale, reasons) is not in any payload. Reassign with auto-withdraw raises `ClaimReassigned` first, then `ProposalWithdrawn` (cause OnReassignment); the outbox `sequence` column (spec 2) keeps that order. Correction and Refused raise no event (ADR-010, ARCHITECTURE section 7). Phase 2 events (`SettlementRequested`, ...) are not defined now.

### 3.11 Supporting types
`Result`/`Result<T>` (success or `DomainRefusal`); `IIdGenerator { Guid NewId(); }` (test seam, sequential in tests; Application provides the GUID implementation); `ClaimNumber` (`Create(market, year, sequence 1..999999)`, `Parse`, `ToString`, regex per BR-25); `MarketCurrency` (six markets to currency, BR-23, keyed by `Contracts.Markets`); `RejectReasonCode` (six values, labels for the Public summary); `ClaimCommand` enum (names of attempts for refusals and Refused summaries); `SettlementCommand` (Request, Confirm, SendBack, Withdraw).

## 4. API (REST via Gateway)
n/a: the domain has no endpoints. Section 3.8 is the contract spec 3 maps to HTTP (type, status). Concurrency (xmin) and idempotency are spec 2. Unmapped refusal types must not exist: spec 3 has a test that every `DomainRefusals` constant maps to a documented problem type in `claims.yaml`.

## 5. Data
n/a (no tables here). Shape handed to spec 2: aggregate fields and child entities of 3.2 and 3.6, `TrailEntry` columns of 3.7 (identical to ARCHITECTURE section 9 `claim_history`), money as `decimal(18,2)` plus `char(3)` currency, status/type/state/visibility stored as strings, `Details` as the two owned tables. Private setters and a private parameterless constructor are the only EF accommodations in the domain. The row version is a shadow property mapped in Infrastructure; the domain has no version field.

## 6. Events
Published: section 3.10, via `Claim.PendingEvents`; Application writes the outbox rows (messaging-engineer reviews the envelope part). Consumed: none (Claims consumes `finance.events` only in Phase 2).

## 7. Claim progress trail
Section 3.7 is the entry catalogue (type, from/to, actor role, visibility, summary). Reason: set for reassign, send-back, withdraw, correction and reject (code and text), propose (rationale). Refused entries: section 3.8, one Internal entry naming actor and attempt, no state change.

## 8. Security
- Authorisation matrix rows at domain level: S11 (dual-role maker confirms own proposal: `maker-cannot-check`, trailed, no state change), S13 (non-assigned officer propose/withdraw/assess/request: `not-assigned-officer` / `not-proposal-maker`), S13b (claimant projection masking), S14 (stale proposalId: `proposal-not-open`, nothing applied). Role-policy cases (S3, S12, S15) are spec 3.
- Threats: maker equals checker (compared on user id, never on roles); mass assignment (no setter, no amount/outcome on confirm); information disclosure through the claimant timeline (closed Public type list, projection exposes three fields, summaries free of outcome while pending); trail tampering (immutable record, no mutation API; DB protection is spec 2).
- Never in summaries, refusals, events or exception messages: free-text responses, bank data (none in Claims), tokens, full names. Refusal `Detail` uses ids and codes only.
- Actor and role come from `Actor` built from the token (BR-11); no domain method accepts an actor id in a payload.

## 9. Dependencies and sequencing
Needs: `Contracts` project (Roles, Markets exist; `Contracts` gains `Roles.System` (trail role only, never a token role) and `Events/` (marker `IClaimEventPayload`, 11 records, ADR-010); `EventEnvelope` is also in `Contracts` but is written in spec 2 with `messaging-engineer`), ADR-005, ADR-009, ADR-010. Unblocks: spec 2 (persistence: aggregate shape, all proposals of a claim loaded, `ReserveBefore`/`ReserveAfter` columns, `TrailEntry`, `DomainRefusal`, `PendingEvents`) and spec 3. No stubs needed. Test project `tests/Claims.Tests` (xUnit, FluentAssertions) references `src/Claims` only.

## 10. Test plan
Unit tests only, in `tests/Claims.Tests/Domain/`. Deterministic: fixed `DateTimeOffset`, sequential `TestIdGenerator`, `ClaimBuilder` that drives real commands to reach each Phase 1 status (Phase 2 statuses set by a test-only reflection helper in the test project; no production seam). Names follow `Method_Scenario_Expected`.

| Criterion / risk | Type | Test name |
|---|---|---|
| AC-4 all 81 pairs, flag off | unit (theory, 81 rows) | `Evaluate_EveryStatusPairWithSettlementDisabled_MatchesTable` |
| AC-4 all 81 pairs, flag on | unit (theory) | `Evaluate_EveryStatusPairWithSettlementEnabled_MatchesTable` |
| AC-4 counts 7/45/29 and 12 | unit | `Evaluate_SettlementDisabled_AllowsExactlyPhaseOneEdges` |
| AC-4 final states | unit | `Evaluate_FromRejectedOrSettled_AlwaysInvalid` |
| AC-4 self pairs | unit | `Evaluate_SameStatus_InvalidTransition` |
| AC-4/AC-17 stubs | unit (theory over 9 statuses x 4 commands) | `RefuseSettlementCommand_AnyStatus_Phase2NotEnabledTrailed` |
| AC-1 | unit | `Submit_ValidPolicyOfCaller_CreatesSubmittedClaimWithReserveEqualEstimate` |
| AC-1 | unit | `Submit_Valid_WritesSinglePublicClaimReceivedEntryByClaimant` |
| AC-1 BR-75 | unit | `Submit_Valid_EntryOccurredAtEqualsSubmittedAt` |
| AC-1 BR-25 | unit | `ClaimNumber_Create_FormatsAsMarketYearSixDigitSequence` |
| AC-1 BR-25 | unit | `ClaimNumber_CreateOrParse_InvalidValues_Rejected` |
| AC-1 BR-23 | unit (theory 6 markets) | `Submit_EachMarket_UsesMarketCurrency` |
| AC-2 | unit | `Submit_PolicyOfAnotherPerson_PolicyNotFound` |
| AC-2 | unit | `Submit_IncidentOutsidePolicyPeriod_Refused` |
| AC-2 | unit | `Submit_IncidentDateInFuture_IncidentDateInFuture` |
| AC-2 | unit | `Submit_IncidentTodayForClaimantAheadOfUtc_Accepted` (now = UTC 23:00 previous day, date = AU local today; edge at +14 h accepted, +14 h and one day refused) |
| AC-2 | unit | `Submit_MarketMismatchAfterOwnershipRule_OwnershipCheckedFirst` |
| AC-1 BR-22 | unit | `Submit_Valid_EntryHasReserveBeforeNullAndReserveAfterEstimate` |
| AC-2 BR-23 | unit | `Submit_CurrencyDifferentFromPolicy_CurrencyMismatch` |
| AC-2 | unit | `Submit_MarketDifferentFromPolicy_MarketMismatch` |
| AC-2 | unit | `Submit_EstimateZeroNegativeOrThreeDecimals_Refused` |
| AC-3 | unit | `PickUp_Submitted_AssignsActorAndWritesPublicEntry` |
| AC-3 BR-26 | unit (theory over other 8 statuses) | `PickUp_AnyOtherStatus_InvalidTransitionTrailed` |
| AC-3 invariant | unit | `PickUp_AfterSuccess_SecondOfficerGetsInvalidTransition` |
| AC-11 | unit | `Reassign_ManagerOnUnderReview_ChangesAssigneeAndWritesInternalEntry` |
| AC-11 | unit (theory over 3 statuses) | `Reassign_ManagerOnOpenAssignedStatuses_Succeeds` |
| AC-11 BR-27 | unit (theory over Submitted, Approved, Rejected, Phase 2) | `Reassign_OnOtherStatuses_InvalidTransitionTrailed` |
| AC-11 | unit | `Reassign_OfficerWhoIsAssignee_HandsOverToNamedOfficer` |
| AC-11 | unit | `Reassign_OfficerNotAssignee_NotAssignedOfficerTrailed` |
| AC-11 | unit | `Reassign_ToCurrentAssignee_SameOfficerUntrailed` |
| AC-11 | unit | `Reassign_ReasonUnderTenCharacters_ValidationError400` |
| AC-11 BR-37 | unit | `Reassign_WhilePending_AutoWithdrawsProposalAndReturnsToUnderReview` |
| AC-11 BR-37 | unit | `Reassign_AfterAutoWithdraw_FormerMakerMayCheckLaterProposalByOther` |
| AC-11 BR-76 | unit | `Reassign_AnySuccess_ClaimStaysAssigned` |
| AC-5 BR-19 | unit (theory) | `NetPayableFor_AssessedExcessLimit_ReturnsFlooredAndCappedAtPolicyLimit` (10,000/500/8,000 -> 8,000; 8,000/500 -> 7,500; 400/500 -> 0; assessed = excess -> 0; limit 0 -> 0; IDR large values; per claim only, no headroom parameter) |
| AC-5 | unit | `Assess_AboveCoverageLimit_ReservePayableCappedAtLimit` |
| AC-5 | unit | `Assess_AssessedEightThousandExcessFiveHundred_SetsPayableAndReserveSevenThousandFiveHundred` |
| AC-5 | unit | `Assess_WritesInternalEntryWithAssessedExcessPayableAndReserveChange` |
| AC-5 | unit | `Assess_Repeated_ReplacesAssessmentAndReserve` |
| AC-5 | unit | `Assess_NotAssignedOfficer_NotAssignedOfficerTrailed` |
| AC-10 | unit | `Assess_WhilePending_ProposalPendingTrailed` |
| AC-5 | unit (theory) | `Assess_OtherStatuses_InvalidTransition` |
| AC-5 | unit | `Assess_NonPositiveOrWrongCurrencyOrTooManyDecimals_Refused` |
| AC-5 BR-22 | unit | `Assess_Success_EntryCarriesReserveBeforeAndAfter` |
| AC-12 | unit | `RequestInformation_AssignedOfficerUnderReview_OpensRequestDueFourteenDays` |
| AC-12 | unit | `RequestInformation_SecondWhileOpen_OpenInfoRequestExists` |
| AC-10/12 | unit | `RequestInformation_WhilePending_ProposalPending` |
| AC-12 | unit | `RequestInformation_BlankQuestion_ValidationError` |
| AC-12 | unit | `AnswerInformationRequest_ClaimantOnInfoRequested_ReturnsToUnderReviewSameOfficer` |
| AC-12 | unit | `AnswerInformationRequest_Success_TrailDoesNotContainResponseText` |
| AC-12 | unit | `AnswerInformationRequest_OtherStatus_InvalidTransition` |
| AC-12 | unit | `AnswerInformationRequest_NotTheClaimant_ClaimNotFoundUntrailed` |
| AC-6 | unit | `ProposeDecision_ApproveWithAssessedAmount_PendingWithInternalAndPublicEntries` |
| AC-6 BR-35 | unit | `ProposeDecision_Success_ReserveAndPayableUnchanged` |
| AC-6 BR-17 | unit (theory 6 codes + invalid) | `ProposeDecision_RejectCode_ValidOnlyForListedCodes` |
| AC-6 | unit | `ProposeDecision_RationaleUnderTenCharacters_ValidationError` |
| AC-6 BR-21 | unit | `ProposeDecision_ApproveWithoutAssessedAmount_AssessedAmountRequired` |
| AC-6 BR-19 | unit | `ProposeDecision_ApproveWithPayableZero_PayableZero` |
| AC-6 BR-18 | unit | `ProposeDecision_PayableAboveEstimateWithoutJustification_JustificationRequired` |
| AC-6 BR-18 | unit | `ProposeDecision_PayableAboveEstimateWithJustification_Succeeds` |
| AC-6 | unit | `ProposeDecision_RejectWithoutAssessedAmount_Succeeds` (decision log 15 Q8) |
| AC-6, AC-20 | unit | `ProposeDecision_FirstProposal_WritesInternalAndPublicEntries` |
| AC-6, AC-20 | unit | `ProposeDecision_SecondProposalAfterSendBack_InternalOnly` |
| AC-6, AC-20 | unit | `ProposeDecision_SecondProposalAfterWithdraw_InternalOnly` |
| AC-6 | unit | `ProposeDecision_NotAssignedOfficer_NotAssignedOfficerTrailed` |
| AC-10 | unit | `ProposeDecision_WhilePending_ProposalPending` |
| AC-6 | unit (theory) | `ProposeDecision_OtherStatuses_InvalidTransition` |
| AC-6 | unit | `ProposeDecision_PublicEntry_HasNoOutcomeAmountOrNames` |
| AC-6 BR-39 | unit | `ProposeDecision_Entries_ShareProposalIdAndOccurredAt` |
| AC-7 | unit | `ConfirmDecision_ApproveByOtherManager_ApprovedReserveEqualsPayable` |
| AC-7 | unit | `ConfirmDecision_RejectByOtherManager_RejectedReserveZero` |
| AC-7 BR-39 | unit | `ConfirmDecision_Success_InternalEntryNamesMakerCheckerAndProposal` |
| AC-7 | unit | `ConfirmDecision_Approve_PublicEntryShowsApprovedAmount` |
| AC-7 | unit | `ConfirmDecision_Reject_PublicEntryShowsCodeLabelAndText` |
| AC-7 BR-32, S11 | unit | `ConfirmDecision_MakerWhoAlsoHoldsManagerRole_MakerCannotCheckTrailedNoChange` |
| AC-7 BR-32 | unit | `ConfirmDecision_SameUserIdDifferentRolesSet_MakerCannotCheck` |
| AC-7 S14 | unit | `ConfirmDecision_StaleProposalId_ProposalNotOpenNothingApplied` |
| AC-7 BR-38 | unit | `ConfirmDecision_SecondConfirmAfterFirst_ProposalNotOpen` |
| AC-7 | unit | `ConfirmDecision_ClaimNotPending_ProposalNotOpen` |
| AC-7 BR-34 | unit | `ConfirmDecision_AppliesStoredProposalExactly_NoCallerSuppliedAmountOrOutcome` (reflection: no such parameter) |
| AC-8 | unit | `SendBackDecision_ByOtherManager_UnderReviewSameAssigneeReserveUnchanged` |
| AC-8 | unit | `SendBackDecision_ByMaker_MakerCannotCheck` |
| AC-8 | unit | `SendBackDecision_ReasonUnderTenCharacters_ValidationError` |
| AC-8, AC-20 | unit | `SendBackDecision_WritesInternalEntryOnly` |
| AC-8 | unit | `SendBackDecision_ThenReproposeByMaker_Succeeds` |
| AC-9 | unit | `WithdrawDecision_ByMaker_UnderReviewInternalEntryOnly` |
| AC-9 S13 | unit | `WithdrawDecision_ByNonMaker_NotProposalMakerTrailed` |
| AC-9 BR-38 | unit | `WithdrawDecision_AfterConfirm_ProposalNotOpen` |
| AC-9 | unit | `WithdrawDecision_ReasonUnderTenCharacters_ValidationError` |
| AC-10 BR-36 | unit | `PendingClaim_AnyMutatingCommand_LeavesStateUnchangedOnRefusal` (snapshot compare) |
| AC-13 | unit | `IssueCorrection_AssignedOfficer_WritesInternalCorrectionEntryOnly` |
| AC-13 | unit (theory over 9 statuses) | `IssueCorrection_AnyStatusIncludingFinal_Succeeds` |
| AC-13 | unit | `IssueCorrection_ManagerNotAssigned_Succeeds` |
| AC-13 | unit | `IssueCorrection_OfficerNotAssignedAndNotManager_NotAssignedOfficerTrailed` |
| AC-13, AC-21 | unit | `IssueCorrection_Success_ChangesNoStatusReserveOrPayableAndRaisesNoEvent` (also asserts no claim field changed, `UpdatedAt` included, via snapshot compare) |
| AC-21 | unit | `TrailEntryRefused_Built_DoesNotTouchAnyClaimField` |
| AC-13 | unit | `IssueCorrection_ReasonShortBlankStatementOrEmptyReference_ValidationError` |
| AC-13 | unit | `IssueCorrection_Entry_IsInternalAndReferencesEarlierEntry` |
| AC-14 BR-05 | unit (theory) | `TrailEntry_Create_MissingMandatoryField_Throws` |
| AC-14 | unit | `TrailEntry_Create_StatusChangeWithoutBothStatuses_Throws` |
| AC-14 | unit | `TrailEntryType_EveryValue_HasExactlyOneVisibility` |
| AC-14 BR-06 | unit | `TrailEntry_PublicType_NeverCarriesMakerCheckerReasonOrReference` |
| AC-14 | unit | `TrailEntry_IsImmutableRecord_HasNoPublicSetters` |
| AC-15 | unit | `TrailEntryRefused_TrailedRefusal_InternalEntryNamesActorAndAttempt` |
| AC-15 | unit | `TrailEntryRefused_UntrailedRefusal_Throws` |
| AC-15 | unit (theory over `DomainRefusals`) | `DomainRefusals_EachType_HasExpectedStatusAndTrailedFlag` |
| AC-15 | unit | `DomainRefusal_Detail_ContainsNoFreeText` |
| AC-16 BR-33, S13b | unit | `Project_FullLifecycleWithSendBack_ReturnsOnlyPublicItems` |
| AC-16 | unit | `Project_DuringPendingProposal_ShowsOnlyDecisionUnderReview` |
| AC-16 | unit | `Project_AfterSendBack_ShowsNoSendBackNoOutcomeNoStaffIdentity` |
| AC-16 | unit | `Project_SerialisedOutput_ContainsNoMakerCheckerProposalIdOrInternalSummary` |
| AC-16 BR-12 | unit | `Project_ShuffledInput_OrdersByOccurredAtThenId` |
| AC-16 BR-09 | unit | `Project_OnlyInternalEntries_ReturnsEmpty` |
| AC-16 BR-33 | unit | `Project_ItemShape_ExposesOnlyIdTimeAndSummary` (reflection; also no reserve fields) |
| AC-16 | unit | `Project_PublicEntryWithReserveFields_DoesNotExposeReserve` |
| AC-19 | unit (theory, 9 rows) | `StatusFor_EachStatus_MapsPendingToVisibleStatus` |
| AC-20 | unit (theory) | `Command_SuccessfulPaths_AddExpectedEntryCount` (first propose +2, confirm +2, all others +1, repeat propose +1) |
| AC-14 | unit | `TrailEntry_Create_OnlyOneReserveFieldSet_Throws` |
| AC-18 BR-22 | unit | `Reserve_ThroughFullHappyPath_FollowsSubmitAssessApproveRules` |
| AC-18 BR-22 | unit | `Reserve_ThroughRejectPath_IsZeroAfterConfirm` |
| AC-18 BR-35 | unit | `Reserve_ProposeSendBackWithdraw_NeverChangesReservePaidOrPayable` |
| AC-18 | unit | `Reserve_AssessedLowerThenHigher_FollowsLatestNetPayable` |
| Money | unit | `Money_Create_MoreThanTwoDecimals_Rejected` |
| Money | unit | `Money_Arithmetic_DifferentCurrencies_Throws` |
| Invariants | unit | `Claim_AfterEveryCommandSequence_HoldsInvariants` (assignee, open proposal/request, reserve sign) |
| Events | unit | `PendingEvents_EachCommand_RaisesExpectedPayloadWithStatusAndWithoutFreeText` (claimantId on the six alert events, currency on DecisionProposed, primitives only, `Version` = 1) |
| Events | unit | `Reassign_AutoWithdraw_RaisesReassignedThenProposalWithdrawnOnReassignmentInThatOrder` |
| Refusal | unit | `FailedCommand_AnyRefusal_LeavesNoPendingEntriesOrEvents` |
| Time/ids | unit | `Commands_UseContextClockAndIdGenerator` (no `Guid.NewGuid()`/`DateTime.UtcNow` in Domain, checked by a source scan test) |

Coverage cross-check with spec 2/3 (not tests here): row-version race (I-series), stored refusals (BR-86), role-policy 403 (S3, S12), startup policy coverage (S32).

## 11. Implementation plan
Each step is a commit (`feat:` or `test:`), tests in the same step, estimate about 75 minutes total of the Claims 4.1 to 4.3 budget.
1. `Result`, `DomainRefusal`, `DomainRefusals`, `ClaimCommand`, `IIdGenerator`, `Actor`, `CommandContext`, `Money`, `MarketCurrency`, `ClaimNumber` + tests (10 min).
2. `ClaimStatus`, `ClaimStateMachine` + the 81-pair theories (10 min).
3. `TrailEntryType`, `TrailEntry`, `TrailSummaries`, `ClaimantTimeline` + tests (15 min).
4. `Policy`, `ClaimDetails`, `Claim.Submit`, `PickUp`, `Assess`, reserve tests (15 min).
5. `InformationRequest`, request/answer (5 min).
6. `Proposal`, propose/confirm/send back/withdraw, maker-checker tests (15 min).
7. `Reassign` with auto-withdraw, `IssueCorrection`, `RefuseSettlementCommand`, event payload records in `Contracts` after architect agreement (10 min).
8. Run the checklist of CODING-GUIDELINES section 15 (build, format, test).
Folders: `src/Claims/Domain/{Claims,Proposals,Trail,Money,Refusals,Events}` (ClaimStatus/Claim/ClaimStateMachine/ClaimNumber/Policy/ClaimDetails/InformationRequest in `Claims`; Proposal/RejectReasonCode in `Proposals`; TrailEntry, TrailEntryType, TrailSummaries, ClaimantTimeline in `Trail`; Money/MarketCurrency in `Money`; Result/DomainRefusal/DomainRefusals/ClaimCommand in `Refusals`; `Actor`, `CommandContext`, `IIdGenerator` in the Domain root). Contracts additions under `src/Contracts/Events/`. Test project `tests/Claims.Tests/Domain/` mirrors the folders. Configuration keys: none in this module (`Settlement:Enabled` and `Idempotency:*` belong to specs 2/3). Cut line: nothing here is cut (domain, trail, maker-checker are MUST); `ProposeDecision` rationale cases and `Reassign` auto-withdraw tests are the last to trim if time is short, never the maker-checker ones.

## 12. Risks, assumptions, open questions
Risks: (1) the aggregate must stay small enough to read (about 20 lines per method); split the rule checks into private guard methods per command, not a rules framework. (2) Entry-count tests must follow decision log 15 (first propose +2, confirm +2, else +1), not a literal "exactly one entry" reading of BR-04. (3) New `Contracts` event types and new problem types touch contracts: architect decides. (4) EF-friendly private setters must not turn into leaky public API; enforced by the no-public-setter test.

Assumptions: assessed amount > 0; the Public final entry for a rejection shows the rationale as the claimant-facing text (BR-08, US-03); reassigning from InfoRequested keeps the open request; the Phase 1 payable cap is the per-claim policy coverage limit (decision log 16); `Settlement:Enabled=true` is not supported in Phase 1.

Closed (answers recorded in section 13 and the decision log):
- Q1 one primary entry plus at most one Public companion: first propose +2, confirm +2, all else +1 (log 15). Q2 reserve before/after on the entry (log 15). Q3 cap at the policy limit per claim, no headroom or confirm re-check (log 15, 16). Q4 Correction publishes no event (ADR-010). Q5 `DomainRefusalException` in Application. Q6 `payable` is a Phase 1 column. Q7 Contracts additions approved with ADR-010 changes. Q8 reject without assessed amount allowed, approve requires it. Q9 Phase 1 reassign statuses are the three open ones. Q10 claimant status masked via `ClaimantView.StatusFor`. Q11 future incident date refused as `incident-date-in-future` with UTC+14 tolerance.

Still open: none for the domain. Notes for other specs: spec 2 loads all proposals of a claim; spec 3 aligns TASKS 4.1 (cap) and adds the new problem types (`incident-date-in-future`, `same-officer`) to `claims.yaml`.

## 13. Review record
| Reviewer | Date | Verdict (REVIEWED / CHANGES REQUESTED) | Notes |
|---|---|---|---|
| architect | 2026-10-28 | CHANGES REQUESTED | The structure is sound and fits ARCHITECTURE sections 3, 3a and 9, ADR-005 and ADR-009: pure Domain; `Result` plus `DomainRefusal`; the trailed flag; the check order; the transition table and the 81-pair counts (7/45/29 and 12/69 verified); maker-checker compared on user id only; amounts derived, never sent; immutable trail with a closed type list; a three-field claimant projection. The changes below come from the BA answers already recorded in `docs/domain/business-rules.md` decision log 15 (items 1 to 4) and from the architect answers (items 5 to 9). Re-review covers only the sections named. |

**Required changes (numbered):**
1. **Public companions (BA Q1, BR-04/BR-08/BR-33).** Write "Decision under review" only on the claim's first decision proposal. Send-back, withdrawal and any later proposal are Internal only (+1); first propose is +2 and confirm is +2. Update AC-6, AC-8, AC-16, the 3.4 Effect cells, the 3.7 table (`DecisionUnderReview`: "first ProposeDecision only") and the 3.7 paragraph. Replace `SendBackDecision_PublicCompanion_IsDecisionUnderReviewOnly` with `SendBackDecision_WritesInternalEntryOnly`, and add `ProposeDecision_SecondProposalAfterSendBack_InternalOnly` and an entry-count theory (+2/+2/+1). The aggregate decides "first" from its own `Proposals` (any earlier proposal with kind Decision), so spec 2 must load every proposal of the claim, not just the open one. This is sound and needs no query in the domain.
2. **Reserve fields on the entry (BA Q2, BR-22).** Add `ReserveBefore` and `ReserveAfter` (`Money?`, claim currency) to `TrailEntry`. Set them on the primary entry of Submit (null to estimate, on the Public Submitted entry), Assess, and Confirm (approve: to payable; reject: to 0). The factory requires both or neither. The fields are allowed on a Public entry but are never projected (AC-16 and `Project_ItemShape_ExposesOnlyIdTimeAndSummary` cover this). ARCHITECTURE section 9 now has the columns `reserve_before` and `reserve_after`.
3. **Remaining coverage limit (BA Q3, BR-19).** Delete "remaining limit equals the coverage limit". In Phase 1 the remaining limit is the coverage limit minus the payables of other Approved claims on the same policy. Make it an input: the Application computes it in claims_db and passes a `CoverageHeadroom` (or `Money otherApprovedPayables`) to `Assess`, `ProposeDecision` and `ConfirmDecision`; the domain runs no query. Propose re-derives the amount with the current headroom instead of reusing a stale stored `NetPayable`. Add tests where another approved claim on the policy reduces the cap. Add open question **Q12 (business-analyst):** at confirm, if the proposal amount is now above the remaining limit (another claim on the policy was approved in between), is the confirm refused (my technical suggestion: 409 `coverage-limit-exceeded`, trailed, a send-back is needed) or the amount capped? BR-34 (apply exactly) points to refusal. Spec 2 takes `SELECT ... FOR UPDATE` on the policy row in approve handling (ARCHITECTURE section 9 confirm row) so two approvals on one policy cannot both pass.
4. **Claimant status masking (BA Q10, BR-33).** Add a pure `ClaimantView.StatusFor(ClaimStatus)` next to `ClaimantTimeline` (PendingDecisionApproval maps to UnderReview; PendingSettlementApproval maps to Approved, ready for Phase 2; all others unchanged), with a 9-row theory test. Spec 3 uses it for the claimant detail and list DTOs. Remove Q10.
5. **Event payloads (ADR-010, answers Q7).** Every payload carries `status` (after the command). `ClaimSubmitted`, `ClaimAssigned`, `InformationRequested`, `InformationProvided`, `ClaimApproved` and `ClaimRejected` carry `claimantId`. `DecisionProposed` carries `currency` with `amount`. Records implement `IClaimEventPayload` with `EventType` and `Version = 1` constants, and use primitives only (no `Money`). Reassign with auto-withdraw raises `ClaimReassigned`, then `ProposalWithdrawn(cause OnReassignment)`, in that order (publish order comes from the outbox `sequence`, spec 2). Add the order to `Reassign_AutoWithdraw_RaisesReassignedAndProposalWithdrawnOnReassignment`.
6. **Problem types (answers Q7 and Q11).** (a) A future incident date is refused with a new 422 `incident-date-in-future`, not `incident-outside-policy-period`, which misleads when the date falls inside the period. "Future" means `IncidentDate > DateOnly.FromDateTime(context.Now.UtcDateTime.AddHours(14))`, so a claimant whose local date is ahead of UTC (AU, UTC+11) is never refused for today. Test both edges. (b) Reassign to the current assignee gives 422 `same-officer`, not trailed (rename `Reassign_ToCurrentAssignee_UnknownOfficer`); `unknown-officer` stays for ids not in `IOfficerDirectory`. (c) The `POST /claims` body has no currency: remove "currency = policy currency" from the Submit payload rules. The Application builds the estimate `Money` in the policy currency, and `currency-mismatch` stays as a domain guard (documented in `claims.yaml`, not reachable through the API). `market-mismatch` stays (US-01 takes the market) and is checked after the ownership rule. Approved without change: `market-mismatch`, `incident-outside-policy-period`, `entry-not-found`, and `validation-error` as the domain backstop. ARCHITECTURE section 9 has been updated.
7. **Correction and refusals do not touch the claim row.** State in 3.4 and 3.7 that `IssueCorrection` and refused attempts set no claim field, `UpdatedAt` included, so they never bump `xmin` and never make a concurrent confirm or pick-up lose. Assert this in `IssueCorrection_Success_ChangesNoStatusReserveOrPayableAndRaisesNoEvent`.
8. **Check order scope (3.4).** Add one sentence: edge validation (FluentValidation 400, for example a short reason) runs before the handler, so "never hidden behind a payload error" holds inside the domain only. This matches BR-27 and BR-79 (format errors are 400 and not trailed).
9. **Close the architect questions in section 12** with the answers below, and drop "assumption, see Q6" on `Payable`. Section 9 dependencies: `Contracts` gains `Roles.System` and `Events/` (marker, 11 records); `EventEnvelope` is also in `Contracts` but is written in spec 2 with `messaging-engineer`.

**Architect answers.**
- **Q4 Correction event: no event.** Trail only, like `RefusedAttempt` (ARCHITECTURE section 7 row added, ADR-010). Why not publish: no consumer needs it, because metrics use the original entries (BR-74) and Notification ignores Internal entries; the payload would carry free text, which ADR-010 forbids; adding it later is an additive outbox change.
- **Q5: confirmed.** The Domain returns `Result`/`DomainRefusal`. `DomainRefusalException` is in Application (ADR-009 "Where the code lives"). The handler throws it when a `Result` fails, and the same type wraps the lost-race translation of `DbUpdateConcurrencyException`, so the idempotency filter has one catch point. The ARCHITECTURE section 9 layering text (which wrongly listed the exception under Domain) and the `docs/specs/README.md` rows 1 and 2 are corrected. Why not a typed `IResult` marker instead of the exception: the lost race already arrives as an exception from `SaveChangesAsync`, so one exception path covers both cases; the exception never enters the Domain (guidelines section 6 allows typed errors at the boundary).
- **Q6: yes, `payable` is a Phase 1 column.** It is null until a confirmed approval, then frozen to the proposal amount, and published in `ClaimApproved`. The section 9 heading that called it Phase 2 is fixed. The BR-41 payable record (inputs, Withdrawn status) is Phase 2.
- **Q7: approved with the changes in items 5 and 6.** Domain may reference `Contracts` payload records (ADR-010); why not domain-owned events plus a mapper: 11 duplicate classes with no gain while the records are plain data. `Roles.System` is approved as a trail role only (never a token role; SECURITY-REQUIREMENTS threat row and S36).
- **Q11: yes, a future incident date is refused**, with its own type and a UTC+14 tolerance (item 6a). If the BA wants it recorded as a business rule, that is theirs to add; technically it is input validation.

**Architectural soundness of the BA-owned items** (already decided in decision log 15): Q1, Q2, Q3, Q8 and Q9 can all be modelled inside the aggregate without cross-service data. Q3 is the only one that reads beyond one aggregate, and it stays inside claims_db (an Application query plus the policy-row lock), so database-per-service holds. Q8 (reject without an assessed amount) and Q9 (reassign statuses) already match the spec. Unaffected and fine to start once the human or orchestrator allows it under the gate: plan steps 1 and 2 (`Result`, refusals, `Money`, `ClaimNumber`, the state machine). |
| architect (re-review) | 2026-10-28 | REVIEWED | All 9 changes are applied. Item 3 is superseded by BA decision log 16 (Phase 1 cap per claim only; the aggregate limit, policy lock and `coverage-limit-exceeded` are Phase 2), and section 3.5 and the refusal table follow it correctly. Q1 to Q11 are closed. The amendments A1 to A5 below are binding and fully specified; the author applies them as text and code in the steps named, with no new decision and no further review. A1 fixes a contradiction that came from my own item 2 wording. A2 to A5 arbitrate claims-persistence Q3. |

**Binding amendments (re-review).**
- **A1 (reserve fields, sections 3.7, AC-14, test list).** "Both or neither" contradicts Submit (`ReserveBefore` null, `ReserveAfter` = estimate). The rule is: `ReserveAfter` is required whenever `ReserveBefore` is set, and `ReserveBefore` may be null only on a `Submitted` entry. Rename `TrailEntry_Create_OnlyOneReserveFieldSet_Throws` to `TrailEntry_Create_ReserveBeforeWithoutAfterOrMissingBeforeOutsideSubmitted_Throws`. The reserve fields are `decimal?` amounts in the claim currency, not `Money?`, because `claim_history` has no currency column and the currency is the claim's. Step 3.
- **A2 (Money mapping, 3.2 and 3.6).** Accepted. `Claim` keeps private `decimal`/`decimal?` backing fields for `Estimated`, `Assessed`, `NetPayable`, `Reserve` and `Payable`, next to the single fixed `Currency`. The public members stay `Money`/`Money?`, composed on read, and all domain logic works on `Money`; no public decimal setter exists. `Proposal` does the same with its own `currency` column. Why not EF 8 complex types: they cannot be optional in EF 8 (three of the five are nullable), and they would create one currency column per amount where BR-23 fixes one per claim. Step 4.
- **A3 (Details, 3.2).** Accepted. Two private nullable owned members (`MotorDetails?`, `PropertyDetails?`), with exactly one set by `Submit`. `Details` and `Type` are computed from them. Why: EF owned types do not support inheritance. Test: `Submit_Details_ExactlyOneOwnedMemberSet`. Step 4.
- **A4 (TrailEntry binding, 3.7).** Accepted. A private constructor that EF binds to; the factories stay the only public way to create an entry, and `TrailEntry_IsImmutableRecord_HasNoPublicSetters` still holds. Step 3.
- **A5 (`Claim.DrainPending()`, 3.2 and 3.3).** Accepted. It returns `(IReadOnlyList<TrailEntry> Entries, IReadOnlyList<IClaimEventPayload> Events)` and clears both lists. The handler calls it exactly once, just before `SaveChanges`; a second call returns empty lists. Test: `DrainPending_SecondCall_ReturnsEmpty`. Steps 4 and 7. |
| architect (code-review ruling) | 2026-10-28 | REVIEWED (amendment A6) | **A6, which replaces the role sentences of section 3.9.** The code review found that the domain never checks the checker's role and that `Claim.Decisions.cs` hard-codes `ActorRole` (Manager for confirm and send-back, Officer for pick-up). Ruling (ADR-005 amended): the domain repeats the role check as defence in depth.<br>- Each command needs its role: Claimant for Submit and Answer; Officer for PickUp, Assess, RequestInformation, Propose and Withdraw; Manager for Confirm and SendBack; Officer or Manager for Reassign and Correction. The settlement stubs need the role of their route.<br>- A missing role gives `DomainRefusals.Forbidden`: 403 `forbidden`, untrailed (`IsTrailed = false`), checked first (step a0, before the Phase 2 gate), with no change to the aggregate.<br>- `ActorRole` on every entry comes from `Actor.Roles` using the 3.9 dual-role mapping, never a literal. `TrailEntry` keeps refusing an `ActorRole` the actor does not hold.<br>- Maker != checker stays on user id only.<br>- Tests: `ConfirmDecision_OfficerOnlyActor_ForbiddenUntrailedNoChange`, `SendBackDecision_OfficerOnlyActor_ForbiddenUntrailed`, a theory `Command_ActorWithoutRequiredRole_ForbiddenUntrailed` over all commands, and `TrailEntries_ActorRole_IsAlwaysARoleTheActorHolds`.<br>Spec 3 needs no change: `forbidden` is already in its `ProblemResults` and is not stored. **Note for the business-analyst (not decided here):** spec 1 shows the maker's free-text reject rationale in the Public "Claim rejected: {code label}, {rationale}" entry (an assumption from the BR-08 example "Not covered, policy excludes flood damage"). BR-33 lists "rationale" as Internal. The BA should say whether the claimant sees the rationale, a separate claimant-facing reason text, or the code label only. Until then the Public summary is left as built and is flagged as open. |
