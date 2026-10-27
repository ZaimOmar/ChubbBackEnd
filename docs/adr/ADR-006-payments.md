# ADR-006: Payments: idempotency, retries, simulated bank gateway, Settling saga and compensation

Status: Accepted. Owner: architect.

**Phasing: the design stands; Finance (Payment, Ledger, Pool Fund) is Phase 2, designed but not built, under the 5-hour cap. Phase 1 builds Gateway, Identity, Claims, Reporting, Notification (five services). Claims keeps extension points so Phase 2 needs no rework (ARCHITECTURE section 0).**

## Context
Settlement must pay exactly once, survive timeouts, and never mark a claim Settled before the bank confirms (BR-44 to BR-53, BR-67). There is no real bank; the demo must still show failure, retry and manager recovery.

## Decision
- **Instruction:** created in Finance when it consumes `SettlementConfirmed` (same transaction as ledger rule 4 and the inbox row). Unique constraint on `claimId` for the active instruction.
- **Idempotency:** key = `paymentId:attemptNo`, unique in `payment_attempt`. A timeout (outcome unknown) retries with the same key and attempt number; the gateway returns the stored outcome. Only a definitive failure advances the attempt number.
- **Simulated gateway:** `IBankGateway` with a fake that honours the idempotency key and is scripted per call (succeed, fail, timeout) by config; a Development-only endpoint toggles it. It is plainly labelled a mock.
- **Dispatch sequence (one local transaction per step, gateway call outside any transaction):** lock currency (advisory lock) -> funds check (balance 1000 minus 2200) -> if short: Held, nothing posted, no retry consumed -> else post rule 5 and mark Processing -> call gateway -> record outcome and post rule 6 (or rule 7) with outbox event.
- **Retries:** DB-driven (`next_attempt_at`, polled with SKIP LOCKED), 4 attempts total at 0, 1, 5, 30 minutes (configurable, seconds in demo). Then Failed, exceptions queue, manager alert; claim stays Settling.
- **Settling saga (choreography, no coordinator):** Claims `Settling` is the visible saga state. Success: `PaymentCompleted` -> Claims Settled, paid recorded. Compensation: manager cancel (Pending/Held/Failed only) -> Finance reverses ledger rule 9 and emits `PaymentCancelled` -> Claims returns Settling->Approved and restores reserve. Bank return after Paid -> `PaymentReversed`: ledger reversed, claim stays Settled (final), manager alerted.

## Alternatives considered
- **Orchestrator service/state machine library (MassTransit saga, Temporal):** two participants do not justify it. Rejected.
- **Synchronous payment inside the settlement confirm request:** blocks the manager and ties the claim to bank latency. Rejected.
- **Hangfire/Quartz for retries:** extra dependency; a polled column is enough and survives restarts.
- **Mark Settled on confirm:** violates BR-48. Rejected.
- **Two-phase commit across Claims and Finance:** not available with Kafka; rejected.

## Consequences
- Exactly-once effect comes from idempotency keys plus unique constraints, not exactly-once delivery.
- A claim can sit in Settling indefinitely if a manager ignores the queue; Reporting shows count and age.
- Real bank integration, re-issue of reversed payments: documented, not built.
