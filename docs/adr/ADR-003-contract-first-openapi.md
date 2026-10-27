# ADR-003: Contract-first OpenAPI and versioned event contracts

Status: Accepted. Owner: architect.

## Context
The backend owns the API; the Angular client is generated from it. Six services and ~25 event types mean the frontend, QA and parallel developers need stable contracts before code exists. Events are consumed by other services written separately.

## Decision
- **REST:** each service (Claims, Finance, Reporting, Notification, Identity) has a hand-reviewed OpenAPI 3 file in `contracts/openapi/<service>.yaml`, written before the controllers. Controllers are written by hand to match. A contract test regenerates the spec from the running code (Swashbuckle) and fails on drift from the committed file. The per-service files are the source of truth.
- **Aggregated document (amended 2026-10-28, TASKS section 3 MUST):** the Gateway serves one merged OpenAPI 3 document at `GET /openapi/v1.json` (plus Swagger UI), and the Angular client is generated from it. The merge input is the **committed** `contracts/openapi/*.yaml` files copied into the Gateway image, not the live downstream `/swagger` endpoints: merge `paths` and `components.schemas`, add the bearer scheme and apply the bearer security requirement to every operation except `POST /auth/token` (per operation rather than document-level, so the exemption needs no override), drop `/internal/**` and `/health/**`. A schema-name collision or a path claimed by two services fails a Gateway test. Served only when `ASPNETCORE_ENVIRONMENT=Development`.
- **Money in contracts:** `amount` as decimal string/number with `currency` (ISO 4217); bank account numbers appear only as `accountLast4`; request DTOs expose only client-settable fields (no `status`, `reserve`, `makerId`).
- **Errors:** RFC 7807 with stable `type` codes (e.g. `maker-cannot-check`, `invalid-transition`, `insufficient-funds`).
- **Idempotency:** mutating money endpoints (`top-ups`, payee details, payment actions) require an `Idempotency-Key` header, documented in the spec.
- **Events:** C# records and a JSON example per type in the `Contracts` project (the only shared code), plus a golden-file test that serialises each event and compares to the stored JSON. Rules: tolerant reader, additive changes only, breaking change = new `version` and both versions consumed during a transition.

## Alternatives considered
- **Code-first only (generate spec from controllers):** fastest, but the frontend waits for the code and the contract changes silently. Rejected as the primary route, kept as the drift check.
- **Generate server stubs from OpenAPI:** strongest guarantee, but stub generators add friction and odd code in a 5-hour build. Rejected.
- **Schema registry (Avro/Protobuf):** correct for production, too heavy here. Documented as the production path.
- **Shared event classes with domain logic:** rejected; only plain records live in `Contracts`.
- **No merged spec, one client per service file (the original decision):** keeps the Gateway dumb, but the frontend then generates and wires four clients with four base paths, and TASKS makes the aggregated document a MUST. Superseded.
- **Gateway fetches and merges live downstream `/swagger` documents at runtime:** always current, but the client generated depends on which services happen to be up (a down service silently drops its paths), the Gateway gains outbound fetch, caching and failure handling, and the frontend cannot generate before the service runs. Rejected: the drift test already guarantees the committed file equals the running code, so merging the committed files gives the same result deterministically.
- **NSwag/Kiota per-service generation with a frontend-side merge:** moves backend contract assembly into the frontend repo. Rejected.

## Consequences
- Writing six specs up front costs about 30-40 minutes; mitigated by writing Claims and Finance first and keeping Notification and Reporting small.
- The drift test must be in CI or the spec rots.
- Frontend can start from the generated client on day one.
- The merged document is only as fresh as the committed files; a service that changes its API must update its file (the drift test forces this) and the Gateway image is rebuilt. Acceptable: contract changes are meant to be deliberate.
- Service contract authors may use only `components.schemas`; the Gateway merge fails on any other component kind (parameters, responses, securitySchemes, etc.), so shared parameters are inlined and security is left to the Gateway. Each service must prefix its schema names (e.g. `ClaimsClaimSummary`, `ReportingExposureRow`) so the merge cannot collide; Gateway paths equal service paths (no rewriting), so the merged document is correct without path transforms.
- The Gateway Docker build context must include `contracts/openapi/` (devops-engineer).
