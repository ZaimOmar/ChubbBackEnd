# ADR-002: REST for commands and queries, Kafka for events

Status: Accepted. Owner: architect.

## Context
Users wait for validation results (a refused self-approval must answer now), while dashboards, alerts, ledger postings and payment dispatch can lag by seconds. With Finance added, Claims and Finance must also exchange facts in both directions without blocking each other.

## Decision
- **REST (via the Gateway)** for every user-facing command and query, including manager payment actions and fund top-ups. The service that owns the data validates and answers synchronously.
- **Kafka** for facts that already happened: `claims.events` (Claims out: Reporting, Notification, Finance in) and `finance.events` (Finance out: Claims, Reporting, Notification in), each with a `.dlq` topic. Key = `claimId` (fund events key = `market-currency`), so per-claim order holds. The envelope is versioned (`eventId, type, version, occurredAt, claimId, market, correlationId, causationId, payload`).
- **No synchronous service-to-service call on the money path.** The only service-to-service REST is the daily reserve reconciliation (Finance asks Claims for reserves), via an internal route not exposed by the Gateway.
- Commands are never sent over Kafka. The only cross-service "commands" are modelled as facts (SettlementConfirmed, then PaymentCompleted), and the receiver reacts.

## Alternatives considered
- **All REST (Claims calls Finance synchronously):** simpler to demo, but a Finance outage would block settlement confirmation, and a timeout leaves "did it pay?" ambiguous. Rejected.
- **All Kafka (request/reply):** loses immediate validation errors. Rejected.
- **gRPC:** no browser benefit and one more toolchain. Rejected.
- **Direct DB reads for dashboards:** violates database-per-service. Rejected.

## Consequences
- Every flow that crosses services is eventually consistent; the visible "Settling" status makes the gap honest to users (see ARCHITECTURE section 8).
- Event schemas are public contracts: additive changes only, `version` bumped on breaking changes (ADR-003).
- Reporting, Notification and Finance dedupe on `eventId` (ADR-004).
- Local runtime needs one broker (KRaft), four topics (two plus DLQs).
