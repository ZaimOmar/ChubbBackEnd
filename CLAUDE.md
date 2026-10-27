# CLAUDE.md – ChubbBackEnd

Backend for the Chubb APAC claims platform (take-home assessment). Spec: `../Assessment/AssessmentSpec.docx`. Task list: `docs/planning/TASKS.md`.
Time guidance: 2–3h target, 5h hard cap. Backend is the priority; frontend is secondary.

## Stack (agreed)
- C# / .NET 8 (SDK 8.0.x installed), ASP.NET Core Web API
- PostgreSQL 16 via Docker, EF Core + Npgsql, migrations auto-applied on startup
- Kafka (KRaft, single broker) via Confluent.Kafka
- API gateway: YARP
- OpenAPI via Swashbuckle (or built-in .NET 8 OpenAPI); the backend owns the contract and the Angular client is generated from it
- Tests: xUnit, FluentAssertions, Testcontainers (Postgres, Kafka)
- Validation: FluentValidation. Logging: Serilog, structured, with correlationId
- Local run: `docker compose up` must start everything with zero manual steps

## Architecture: microservices (each independently deployable)
Six services in the design. **Phase 1 (build, 5h cap):** Gateway, Identity, Claims, Reporting, Notification. **Phase 2 (designed, not built, kept in all diagrams):** Finance. See `docs/architecture/ARCHITECTURE.md` section 0 and `docs/architecture/MODULES.md`; decisions in `docs/adr/`.
| Service | Owns | Talks via |
|---|---|---|
| `Gateway` (YARP) | routing, JWT validation, CORS | REST in, REST out |
| `Identity` | mock users and roles (Claimant, Officer, Manager), dev JWT issuing | REST |
| `Claims` | claims, policies, info requests, proposals (maker-checker), claim progress trail (append-only), assignment, outbox | REST in, Kafka out |
| `Reporting` | read model: outstanding counts, liability exposure, officer workload and performance | Kafka in, REST out (+ SSE) |
| `Notification` | claimant/manager alerts (inbox + log stub) | Kafka in |
| `Finance` (**Phase 2, not built**) | Payment, General Ledger, Pool Fund modules in one service (schemas in `finance_db`); simulated bank gateway; payee bank details | REST in, Kafka in/out |

Rules:
- Database per service (separate Postgres databases on one instance). No cross-service DB access or shared tables.
- Commands and queries are REST. State changes publish domain events to Kafka. Reporting and Notification only consume.
- Claims uses a transactional outbox; a background publisher sends outbox rows to Kafka (at-least-once). Consumers are idempotent (processed-events table keyed by eventId).
- Topics `claims.events` (key = claimId, preserves per-claim ordering) and `claims.events.dlq`; Phase 2 adds `finance.events` and its DLQ.
- Event envelope: `eventId, type, version, occurredAt, claimId, market, payload`. Event list and phases: `docs/architecture/ARCHITECTURE.md` events table (Phase column).
- Shared code lives only in a small `Contracts` project (event types, constants). No shared domain or EF code.

## Domain
- Claim types: Motor, Property. Markets: SG, HK, MY, TH, ID, AU. Currency per claim; Reporting converts to USD with a static FX table.
- Status lifecycle (nine states): Submitted → UnderReview ↔ InfoRequested → PendingDecisionApproval → Approved or Rejected; Phase 2: Approved → PendingSettlementApproval → Settling → Settled. Authoritative: `docs/domain/business-rules.md` (BR-01) and `docs/architecture/ARCHITECTURE.md`.
- **Maker-checker:** the assigned officer proposes the decision; a Manager (never the maker) confirms or sends back. Enforced in the domain (ADR-005). Settlement states exist in the enum but are disabled (`Settlement:Enabled=false`, 409 `phase-2-not-enabled`).
- **Claim progress trail is the assessment centrepiece:** every transition, assignment, info request/answer, assessment and refused attempt writes one immutable Public or Internal entry in the same transaction as the state change and outbox row.
- The state machine lives in the Domain layer, not the controllers, and is unit-tested. Invalid transitions return 409/422 problem details.
- Liability exposure = outstanding reserve on open claims (not Rejected or Settled), by market, type and status (glossary). Amounts are net of excess.
- Pick-up is concurrency-safe: no double assignment (rowVersion or `FOR UPDATE SKIP LOCKED`), conflict returns 409.
- Claimants may only read their own claims.

## Layout
```
ChubbBackEnd/
  src/
    Gateway/  Identity/  Claims/  Reporting/  Notification/  Contracts/
    Finance/   (Phase 2, not built)
  tests/
  docker-compose.yml
  docs/adr/   docs/journal/AI-JOURNAL.md
```
Inside each service keep it pragmatic: `Api`, `Application`, `Domain`, `Infrastructure` folders or projects, not layers for their own sake.

## Conventions
- Follow `docs/guidelines/CODING-GUIDELINES.md` (binding for all code). Agent roster and workflow: `AGENTS.md`.
- Errors: RFC 7807 problem details. Pagination on list endpoints. Money as `decimal(18,2)` plus ISO 4217 currency.
- Endpoints, per spec of the task list: see `docs/planning/TASKS.md` section "API".
- Small conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`). Never one mega-commit.
- Append to `docs/journal/AI-JOURNAL.md` after each phase: what was asked, accepted, challenged, overridden, and why.
- Write an ADR (half a page) for each major decision in `docs/adr/`.
- Source documents, in order: `docs/product/problem-statement.md`, `docs/product/user-stories.md`, `docs/domain/` (business rules, owned by business-analyst), `docs/architecture/ARCHITECTURE.md` + `docs/adr/`, `docs/security/SECURITY-REQUIREMENTS.md`.
- Do not add features outside `docs/planning/TASKS.md`. Cut COULD items first. Phase 2 items are designed, not built.

## Build order (cut from the end if time runs short)
1. Scaffold solution, docker-compose (Postgres, Kafka), Contracts, ADRs, journal
2. Claims: domain and state machine, EF, seed data, REST endpoints, tests
3. Outbox and Kafka publisher
4. Identity and Gateway (mock JWT, role policies)
5. Reporting consumer and endpoints
6. Notification consumer
7. Integration tests, README, walkthrough notes

## Risks to keep in mind
- Five services is a lot for the time cap. Keep Identity, Gateway and Notification deliberately thin; Claims and Reporting get the depth.
- Never let the frontend wait on unfinished services: Claims must be demoable end to end before the others.
