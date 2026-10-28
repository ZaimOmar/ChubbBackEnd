# SPEC: Notification  (service: Notification)

| Field | Value |
|---|---|
| Author | backend-developer |
| Status | IN REVIEW (code first, review after; human decision: minimal spec, no tests yet) |
| Phase | P1 build |
| Stories / rules | US-03, US-24, US-30; BR-08, BR-33, BR-64, BR-65 |
| Tasks / ADRs | TASKS section 7; ADR-002, ADR-004, ADR-010 |

## 1. Purpose and scope
Consumes `claims.events` (group `notification`) and keeps an inbox: claimant alerts and manager alerts, plus a log-only delivery stub (no email or SMS).
Out of scope: info-request/answer alerts (no such events in Contracts yet), claim numbers (only `ClaimSubmitted` carries one), Phase 2 money alerts, tests.

## 2. Rules
| Event (v1) | Recipient | Public text |
|---|---|---|
| ClaimAssigned | claimant (`claimantId` in payload) | "Your claim has been picked up for review." |
| DecisionProposed | role Manager | "A decision proposal awaits your check." (BR-65; the claimant is never told, BR-33) |
| ClaimApproved | claimant | "Your claim has been approved for {currency} {payable}." (confirmed only) |
| ClaimRejected | claimant | "Your claim has been rejected." (no reason or rationale) |
Other types and versions are logged and ignored.

## 3. Consumer and data
Manual commit after processing; per message the notification row and `processed_events(event_id PK)` are saved in one transaction; 3 attempts with backoff, then `claims.events.dlq` with error headers (class only); no payloads in logs. Same approach as the Reporting consumer.
Schema `notification` in `notification_db`: `notifications` (id, recipient_user_id xor recipient_role, kind, claim_id, message, created_at, read_at; indexes on recipient + created_at desc) and `processed_events`. Migrations run at startup; with no `ConnectionStrings:NotificationDb` or `Kafka:BootstrapServers` the migrations and consumer are skipped with a warning.

## 4. API (contract: contracts/openapi/notification.yaml)
| Route | Policy | Behaviour |
|---|---|---|
| GET /notifications?page&pageSize | `notification-inbox` (Claimant, Manager) | Claimant: own rows only (user id from token). Manager: manager alerts. Newest first, pageSize max 100. |
| POST /notifications/{id}/read | `notification-inbox` | 204; repeating is harmless so no Idempotency-Key; outside the caller's scope gives 404. |
Errors are RFC 7807 with correlationId. Officers get 403.

## 5. Security
Messages hold Public text only. Delivery stub and logs carry ids and kind, never message text. Manager alerts are shared by all managers (one read state).

## 6. Risks and open questions
Unconfirmed: whether Officers should see assignment alerts (assumed no); shared read state for managers; gateway 401/403 bodies are empty (JwtBearer defaults).

## 7. Review record
| Reviewer | Date | Verdict | Notes |
|---|---|---|---|
| architect | pending | pending | Review after code, per human decision |
