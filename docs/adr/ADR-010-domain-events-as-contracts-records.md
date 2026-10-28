# ADR-010: Claims domain raises `Contracts` event payload records directly

Status: Accepted (architect, 2026-10-28, claims-domain SPEC review Q4 and Q7). Owner: architect; envelope and publisher side reviewed by `messaging-engineer`. Related: ADR-003 (versioned event contracts), ADR-004 (outbox).

## Context
The Claims aggregate produces the events that the Application layer writes to the outbox (`Claim.PendingEvents`). Either the Domain defines its own event classes and Application maps them to the wire records in `Contracts`, or the Domain creates the `Contracts` payload records itself. Phase 1 has 11 event types, and the two class sets would be near-identical. The spec also asked whether a trail `Correction` should be published, and whether `Roles.System` belongs in `Contracts`.

## Decision
- **`Chubb.Claims.Domain` may reference `Chubb.Contracts`** and adds the payload records from `Chubb.Contracts.Events` (for example `ClaimApproved`) to `PendingEvents`. The Application outbox writer wraps each record in the `EventEnvelope` (also in `Contracts`) and adds `eventId`, `occurredAt`, `claimId`, `market`, `correlationId` and `causationId`. The reference goes one way only: `Contracts` never references Domain.
- **What a payload record may contain:** only `Guid`, `string`, `decimal`, `int`, `DateOnly`, `DateTimeOffset` and enums-as-strings. No `Money` or other Domain types (money is `amount` plus `currency`), no serializer or Kafka attributes, no free text (question, response, rationale, reasons, corrected statement). Each record implements the marker interface `IClaimEventPayload` and declares `const string EventType` and `const int Version = 1`. The golden-file test of ADR-003 covers every record.
- **Every Claims payload carries `status`**, the claim status after the command, so a Reporting snapshot is an upsert and does not need to infer status from the event type (that inference breaks when Phase 2 adds types). Payloads that trigger a claimant alert (`ClaimSubmitted`, `ClaimAssigned`, `InformationRequested`, `InformationProvided`, `ClaimApproved`, `ClaimRejected`) also carry `claimantId`, so Notification needs no lookup and no predecessor.
- **When one command raises several events** (reassign with auto-withdraw raises `ClaimReassigned`, then `ProposalWithdrawn`), the order of `PendingEvents` is the publish order. The outbox gets a `sequence bigint GENERATED ALWAYS AS IDENTITY` column and the publisher orders by it, because rows from one transaction share `occurred_at`.
- **Not published:** `Correction` and `Refused` trail entries. No consumer needs them: Reporting metrics use the original entries (BR-74), and Notification never alerts on Internal entries (MODULES section 4). We read the BA impact note "add event type Correction" as a trail entry type, not a Kafka event.
- **`Roles.System = "System"`** is added to `Contracts.Roles` for trail `actorRole` on machine actions (BR-11). It is never a token role: Identity seeds no System user, and no Claims, Reporting or Notification policy accepts it (see the SECURITY-REQUIREMENTS threat row).

## Alternatives considered
- **Domain-owned event classes plus a mapper in Application.** This keeps the Domain free of wire types, but it means 11 duplicate classes and 11 mappings that can drift, with no gain while the records are plain data. We would switch to it if a payload ever needs a shape the domain should not know about. Rejected for now.
- **Publish `TrailEntryCorrected` (ids only)** for a future audit or search consumer. No consumer exists (YAGNI), and adding it later is an additive outbox change. Rejected.
- **Event-carried state transfer (a full claim snapshot on every event).** Larger payloads, and money and personal fields would spread to every consumer. Rejected; `status` plus the fields each consumer needs is enough.
- **Infer status in Reporting from the event type.** Brittle (reassign with auto-withdraw, Phase 2 types). Rejected.

## Consequences
- A breaking payload change needs a new record version (for example `ClaimApprovedV2`) that is consumed alongside the old one. The Domain changes with it, which is acceptable because ADR-003 already makes such changes deliberate.
- The `Contracts` project grows by `Events/` (envelope, marker interface, 11 records). It stays free of logic.
- Outbox migration (spec 2): add the `sequence` column; the publisher uses `ORDER BY sequence` with `FOR UPDATE SKIP LOCKED`.
