# ADR-004: Async reliability: transactional outbox, idempotent consumers, DLQ

Status: Accepted. Owner: architect.

## Context
Claims and Finance both change state and must tell others (Claims -> Finance for the payment, Finance -> Claims for PaymentCompleted). Kafka delivery is at-least-once. A lost event means a claim stuck in Settling or a ledger that disagrees with the claim; a duplicate must never pay twice or post twice.

## Decision
- **Transactional outbox in Claims and in Finance.** State change, trail entry (Claims) and outbox row commit in one transaction; a BackgroundService polls unpublished rows (`FOR UPDATE SKIP LOCKED`) and publishes keyed by `claimId`, marking them sent only after the broker acknowledges.
- **Idempotent consumers everywhere.** Each consumer inserts `eventId` into `processed_events` in the same transaction as its effect; a duplicate hits the unique key and is skipped. Finance gets a second guard: `ledger.journal_entry.source_event_id` is unique, and `payment.idempotency_key` is unique, so even a bug in the inbox cannot double-post or double-pay.
- **Retry then DLQ.** A consumer retries transient failures (3 times, backoff), then publishes the original message plus the error and consumer name to `<topic>.dlq` and commits the offset so the partition keeps moving. Poison cases include: malformed JSON, unknown version, `SettlementConfirmed` whose amount differs from the payable received at approval, and `PaymentCompleted` for a claim not in Settling. DLQ depth raises a manager alert (ReconciliationBreak).
- **Optimistic concurrency:** `rowVersion` on Claim and on Payment; a lost race returns 409. In Claims the row version is Postgres `xmin` mapped with `IsRowVersion()`.
- **Idempotent commands (API side):** a client resend of a Claims command is deduplicated by `Idempotency-Key`, recorded in the command transaction (ADR-009). This is separate from the consumer `processed_events` guard.
- **Ordering:** key = `claimId`; consumers must tolerate a missing predecessor by parking the event to retry, not by guessing.

**Amended 2026-10-28 (architect, after the messaging-engineer outbox review).**
- **Constants in `Contracts`:**
  - `Topics` holds `ClaimsEvents = "claims.events"` and `ClaimsEventsDlq = "claims.events.dlq"`. The finance topics are added with Phase 2, not before.
  - `KafkaHeaderNames` holds `EventId = "event-id"`, `EventType = "event-type"` and `EventVersion = "event-version"`. They live in a separate class from the HTTP `HeaderNames` so the two header kinds cannot be confused.
  - Kafka headers copy envelope fields so DLQ triage and routing can work without parsing. The envelope stays the source of truth: a consumer that uses a header must check it equals the envelope, and DLQ the message on a mismatch.
- **Poison outbox row (producer side):**
  - On failure: `attempts + 1`, then a sanitised `last_error` truncated to 1,000 characters, then a capped exponential backoff of 1 s up to 60 s.
  - After 10 attempts, or straight away on a non-retryable error, the publisher stops at that row. It logs `OutboxStuck` and reports a Degraded health check.
  - The row is never DLQ'd. ARCHITECTURE section 5 has the rule; section 12 records the head-of-line trade-off.
  - Why not a producer DLQ: it would lose or reorder a fact that has already been committed. That breaks "never saved without its event".
- **"DLQ depth raises a manager alert (ReconciliationBreak)" applies to Phase 2.** `ReconciliationBreak` is a Finance event, so Phase 1 has no manager alert. In Phase 1 each consumer (Reporting, Notification) that dead-letters a message:
  - logs one structured Error `EventDeadLettered` (consumer, topic, partition, offset, eventId, type, error class; no payload)
  - reports Degraded on its health check for 15 minutes after its last dead-letter
  - Inspection is through the log, or kafka-ui if the optional profile is on. Manual replay is not built, as before.

## Alternatives considered
- **Dual write (save then publish):** loses events on crash. Rejected.
- **CDC (Debezium):** robust but another component to run. Documented for production.
- **Exactly-once Kafka transactions:** does not cover the database side effect. Rejected; idempotent consumers are simpler and sufficient.
- **Skip outbox for Finance, publish directly:** a PaymentCompleted lost after a bank payout is the worst bug in the system. Rejected.

## Consequences
- Latency of up to the outbox poll interval (1 s) per hop.
- Every consumer needs a `processed_events` table and a DLQ path, tested with Testcontainers (security test S8, S21).
- DLQ messages need an operational owner; here, a manager alert and a documented manual replay (not built).
