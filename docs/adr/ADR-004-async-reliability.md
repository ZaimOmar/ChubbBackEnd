# ADR-004: Async reliability: transactional outbox, idempotent consumers, DLQ

Status: Accepted. Owner: architect.

## Context
Claims and Finance both change state and must tell others (Claims -> Finance for the payment, Finance -> Claims for PaymentCompleted). Kafka delivery is at-least-once. A lost event means a claim stuck in Settling or a ledger that disagrees with the claim; a duplicate must never pay twice or post twice.

## Decision
- **Transactional outbox in Claims and in Finance.** State change, trail entry (Claims) and outbox row commit in one transaction; a BackgroundService polls unpublished rows (`FOR UPDATE SKIP LOCKED`) and publishes keyed by `claimId`, marking them sent only after the broker acknowledges.
- **Idempotent consumers everywhere.** Each consumer inserts `eventId` into `processed_events` in the same transaction as its effect; a duplicate hits the unique key and is skipped. Finance gets a second guard: `ledger.journal_entry.source_event_id` is unique, and `payment.idempotency_key` is unique, so even a bug in the inbox cannot double-post or double-pay.
- **Retry then DLQ.** A consumer retries transient failures (3 times, backoff), then publishes the original message plus the error and consumer name to `<topic>.dlq` and commits the offset so the partition keeps moving. Poison cases include: malformed JSON, unknown version, `SettlementConfirmed` whose amount differs from the payable received at approval, and `PaymentCompleted` for a claim not in Settling. DLQ depth raises a manager alert (ReconciliationBreak).
- **Optimistic concurrency:** `rowVersion` on Claim and on Payment; a lost race returns 409.
- **Ordering:** key = `claimId`; consumers must tolerate a missing predecessor by parking the event to retry, not by guessing.

## Alternatives considered
- **Dual write (save then publish):** loses events on crash. Rejected.
- **CDC (Debezium):** robust but another component to run. Documented for production.
- **Exactly-once Kafka transactions:** does not cover the database side effect. Rejected; idempotent consumers are simpler and sufficient.
- **Skip outbox for Finance, publish directly:** a PaymentCompleted lost after a bank payout is the worst bug in the system. Rejected.

## Consequences
- Latency of up to the outbox poll interval (1 s) per hop.
- Every consumer needs a `processed_events` table and a DLQ path, tested with Testcontainers (security test S8, S21).
- DLQ messages need an operational owner; here, a manager alert and a documented manual replay (not built).
