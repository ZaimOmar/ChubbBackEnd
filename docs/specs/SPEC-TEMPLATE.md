# SPEC: <Module name>  (service: <Claims | Reporting | Notification | Identity | Gateway | Finance>)

Copy this file to `docs/specs/<module>/SPEC.md` and fill every section before writing code. Keep it short (1–2 pages); delete a section only by writing "n/a" and why.

| Field | Value |
|---|---|
| Author | <developer agent> |
| Status | DRAFT → IN REVIEW → **REVIEWED** (by `architect`) → IMPLEMENTED |
| Phase | P1 build / P2 designed-not-built |
| Stories (US-xx) | |
| Business rules (BR-xx) | |
| Tasks (docs/planning/TASKS.md) | |
| Related ADRs | |

## 1. Purpose and scope
What this module does and why, in 3–5 lines. **In scope:** … **Out of scope / not now:** …

## 2. Acceptance criteria
Copy the Given/When/Then criteria from the stories this module serves. Each one must map to at least one test in section 10.

## 3. Domain model and invariants
Entities, value objects, states and the transitions this module owns. Invariants that must always hold (e.g. maker ≠ checker, trail entries immutable). Where each rule lives (Domain layer).

## 4. API (REST via Gateway)
| Method + route | Caller roles (auth policy) | Request | Success | Errors (problem-details type, status) |
|---|---|---|---|---|
Object-level rules (e.g. claimant only own claim). Idempotency and concurrency (rowVersion, 409 cases).

## 5. Data
Tables/columns/indexes added or changed, migration notes, seed data. Money as decimal + ISO currency. Database owned by this service only.

## 6. Events
Published (type, key, payload fields, when, written via outbox in the same transaction) and consumed (type, idempotency key, effect, DLQ behaviour). Event names must match the contract in `Contracts` / ARCHITECTURE events table.

## 7. Claim progress trail
Which trail entries this module writes (event type, from/to status, actor/role, summary, reason, visibility Public|Internal), including refused attempts.

## 8. Security
Rows of the authorisation matrix this module implements; threats from SECURITY-REQUIREMENTS.md it must mitigate; security tests (S-ids) that will cover it; data that must never be logged or returned (PII, bank details).

## 9. Dependencies and sequencing
Other modules/services/contracts needed first; stubs allowed; what this unblocks.

## 10. Test plan
| Criterion / risk | Test type (unit / integration / contract) | Test name (`Method_Scenario_Expected`) |
|---|---|---|

## 11. Implementation plan
Ordered small steps (each one a commit), files/folders to create, configuration keys. Estimate vs the 5h cap and the cut line.

## 12. Risks, assumptions, open questions
Anything unclear. Unresolved questions go to the owner (`product-owner` for scope, `business-analyst` for rules, `architect` for design) **before** coding.

## 13. Review record
| Reviewer | Date | Verdict (REVIEWED / CHANGES REQUESTED) | Notes |
|---|---|---|---|
