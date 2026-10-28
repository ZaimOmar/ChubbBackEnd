# SPEC: Reporting read model, consumer and dashboards  (service: Reporting)

| Field | Value |
|---|---|
| Author | backend-developer |
| Status | IN REVIEW (code first, review after) |
| Phase | P1 build |
| Stories | US-13, US-14, US-30, US-10, US-24 |
| Business rules | BR-22, BR-24, BR-28, BR-35, BR-76, BR-77 |
| Tasks | TASKS section 6 (first four MUST/SHOULD items) |
| ADRs | ADR-002, ADR-004, ADR-010 |

## 1. Scope
Reporting consumes `claims.events` (group `reporting`) and projects them into `reporting_db`; it serves manager dashboards and officer workload over REST. It holds no business rules and never calls Claims.
Out of scope: team performance, handling time, SSE, Phase 2 settlement events, replay of DLQ messages. Cut line: workload is built last; performance/SSE are not started.

## 2. Endpoints (all via Gateway, RFC 7807 errors, each with a named policy)
| Route | Policy | Result |
|---|---|---|
| `GET /dashboard/outstanding` | Manager | open count, counts by status (Pending* separate, zero-filled), market, type, awaiting-checker subtotal, unassigned count, oldest unassigned (Submitted, no assignee; submittedAt then claim number) |
| `GET /dashboard/exposure` | Manager | open reserve in USD by market and type, FX table date; Rejected excluded; Pending* at current reserve |
| `GET /dashboard/pending-approvals?page&pageSize` | Manager (Officer gets 403) | checker queue, oldest proposal first: claim number, market, type, amount, currency, makerId, proposedAt, ageHours |
| `GET /staff/workload` | Officer (own) / Manager (all officers) | open claims per officer by status with oldest age in days; Pending* flagged `waitingOnChecker` |

## 3. Read model (schema `reporting`)
- `claim_snapshot` (PK claim_id): number, market, type, currency, status, reserve decimal(18,2), assignee_id, submitted_at, last_event_at. Indexes (status, market), assignee_id, submitted_at.
- `pending_proposal` (PK proposal_id, unique claim_id): maker_id, amount?, currency, proposed_at.
- `processed_events` (PK event_id, processed_at).

## 4. Consumer rules
- Manual commit after processing; tolerant reader of `EventEnvelope` (unknown fields ignored); unknown type or version is logged (type/version only) and skipped.
- One SaveChanges per event writes the projection and `processed_events`; a duplicate event id is skipped. Events older than the snapshot's `last_event_at` are skipped.
- Reserve: submitted = reserve, assessed = reserveAfter, approved = payable, rejected = 0, proposals/send-backs leave it unchanged (BR-22, BR-35).
- 3 attempts with backoff, then `claims.events.dlq` with original headers plus error headers, one `EventDeadLettered` error log (no payload), health Degraded for 15 minutes, commit.
- Without a connection string or `Kafka:BootstrapServers` the migration and consumer are skipped with a warning.

## 5. Security and risks
Dashboards Manager only; workload scoped to the caller for Officers. No personal data stored or logged.
Risks: no `ClaimReassigned`/`ProposalWithdrawn` event exists in Contracts yet, so reassignment and withdrawal are not projected. Open question for architect: the missing-predecessor case goes through retry then DLQ.
