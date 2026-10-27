# ADR-001: Microservices and the service split (six services, Finance as one service with three modules)

Status: Accepted (Finance grouping needs human confirmation, see Consequences). Owner: architect.

**Phasing: the design stands; Finance (Payment, Ledger, Pool Fund) is Phase 2, designed but not built, under the 5-hour cap. Phase 1 builds Gateway, Identity, Claims, Reporting, Notification (five services). Claims keeps extension points so Phase 2 needs no rework (ARCHITECTURE section 0).**

## Context
The assessment asks for a claims platform with a progress trail and manager dashboards. The human settled five services (Gateway, Identity, Claims, Reporting, Notification) and then added three money capabilities: a Payment service (own service per the human), a double-entry General Ledger, and a separate Pool Fund module. Hard cap is 5 hours, so every extra deployable has a real cost (Dockerfile, compose entry, health check, JWT config, Kafka consumer, tests, OpenAPI).

## Decision
Six services: Gateway, Identity, Claims, **Finance**, Reporting, Notification. Finance is one deployable with three modules, each in its own Postgres schema inside `finance_db` and its own folder/assembly: **Payment** (`payment` schema), **Ledger** (`ledger`), **PoolFund** (`fund`). Modules talk only through in-process interfaces (`IPostingService`, `IBalanceQuery`), never by reading each other's tables; an architecture test (NetArchTest) enforces it. The fund has no balance table: balance = ledger account 1000. Payment keeps its own API, scheduler and state machine, so it can be lifted into its own service later without changing its contracts (it already talks to Ledger through an interface and to Claims through events).

Why Finance is one service:
- Payment dispatch, the funds check, and ledger posting must be atomic. Inside one service and one database they are one local transaction (plus an advisory lock per currency to stop two payments both passing the funds check). Across services this becomes a distributed saga with its own compensation for every posting.
- The fund balance is a ledger balance; a separate Pool Fund service would only proxy the ledger.
- Cost: 6 deployables in the full design (5 in Phase 1), not 8.

## Alternatives considered
- **Three separate services (Payment, Ledger, Fund):** purest boundaries, matches the human's "own Payment service" literally. Rejected: eight services in 5 hours, a posting saga per payment attempt, funds check becomes a cross-service call with a race, and a chatty ledger/fund pair. Revisit if the team grows.
- **Put all three into Claims:** fewest deployables. Rejected: Claims would own claim state, bank details, retries against a bank, and the financial record; a gateway outage or retry storm would share a process and release cycle with claim intake, bank details would live next to claim data, and "ledger is append-only, fed only by events" would be unenforceable.
- **Payment as its own service, Ledger+Fund together:** defensible, but the atomic payment/funds/posting path is exactly the one that needs to be local. Rejected for the same reason.

## Consequences
- Deviation to confirm: the human said "Own Payment service"; Payment is its own module with its own schema, API and scheduler, not its own process. The human must confirm.
- Finance has two roles on Kafka: consumes `claims.events`, publishes `finance.events`; Claims consumes `finance.events` (PaymentCompleted etc.). Two topics, no synchronous Claims-Finance call on the money path.
- A bug in Finance's modules shares a process; mitigated by module isolation tests.
- Frontend: new Gateway routes `/payments`, `/ledger`, `/funds`, `/claims/{id}/payee-details`.
- General rules unchanged: database per service, REST for commands/queries, Kafka for events.
