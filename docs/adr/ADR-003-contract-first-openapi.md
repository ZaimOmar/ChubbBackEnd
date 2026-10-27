# ADR-003: Contract-first OpenAPI and versioned event contracts

Status: Accepted. Owner: architect.

## Context
The backend owns the API; the Angular client is generated from it. Six services and ~25 event types mean the frontend, QA and parallel developers need stable contracts before code exists. Events are consumed by other services written separately.

## Decision
- **REST:** each service (Claims, Finance, Reporting, Notification, Identity) has a hand-reviewed OpenAPI 3 file in `contracts/openapi/<service>.yaml`, written before the controllers. Controllers are written by hand to match. A contract test regenerates the spec from the running code (Swashbuckle) and fails on drift from the committed file. The Gateway publishes no merged spec; the Angular client is generated per service file.
- **Money in contracts:** `amount` as decimal string/number with `currency` (ISO 4217); bank account numbers appear only as `accountLast4`; request DTOs expose only client-settable fields (no `status`, `reserve`, `makerId`).
- **Errors:** RFC 7807 with stable `type` codes (e.g. `maker-cannot-check`, `invalid-transition`, `insufficient-funds`).
- **Idempotency:** mutating money endpoints (`top-ups`, payee details, payment actions) require an `Idempotency-Key` header, documented in the spec.
- **Events:** C# records and a JSON example per type in the `Contracts` project (the only shared code), plus a golden-file test that serialises each event and compares to the stored JSON. Rules: tolerant reader, additive changes only, breaking change = new `version` and both versions consumed during a transition.

## Alternatives considered
- **Code-first only (generate spec from controllers):** fastest, but the frontend waits for the code and the contract changes silently. Rejected as the primary route, kept as the drift check.
- **Generate server stubs from OpenAPI:** strongest guarantee, but stub generators add friction and odd code in a 5-hour build. Rejected.
- **Schema registry (Avro/Protobuf):** correct for production, too heavy here. Documented as the production path.
- **Shared event classes with domain logic:** rejected; only plain records live in `Contracts`.

## Consequences
- Writing six specs up front costs about 30-40 minutes; mitigated by writing Claims and Finance first and keeping Notification and Reporting small.
- The drift test must be in CI or the spec rots.
- Frontend can start from the generated client on day one.
