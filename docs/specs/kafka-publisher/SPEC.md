# SPEC: Kafka outbox publisher  (service: Claims)

| Field | Value |
|---|---|
| Author | messaging-engineer |
| Status | IN REVIEW (code first, review after; human decision 2026-10-29) |
| Phase | P1 build |
| Stories (US-xx) | US-24 (events reach Reporting and Notification), US-25 (zero-step compose) |
| Business rules (BR-xx) | BR-04, BR-07 (event never saved without state, never lost) |
| Tasks | TASKS section 5 (topics, compose init), "Kafka outbox publisher" in Phase 1 focus, step 4 of the build order |
| Related ADRs | ADR-004 (and its 2026-10-28 amendment), ADR-010, ARCHITECTURE s5, s7, s9, s10 |

## 1. Purpose and scope
Move committed `claims.outbox` rows to Kafka `claims.events`, at least once, in `sequence` order, keyed by claim, so per-claim order holds. **In scope:** `BackgroundService` in `src/Claims/Infrastructure/Outbox` using only `IOutboxStore` + `ICommandTransaction`; Confluent.Kafka producer; message shape; `Topics` and `KafkaHeaderNames` in `Contracts`; `outbox` health check; compose `kafka`, `kafka-init`, Claims wiring. **Out of scope:** consumers (Reporting, Notification), Finance topics, producer-side DLQ (forbidden by ADR-004), kafka-ui (COULD, not built), tests (code-first decision; test plan in section 10 is for later).

## 2. Acceptance criteria
- AC-1 A backlog (the seeded rows) is published in `sequence` order per claim and every row gets `published_at`.
- AC-2 A failed send stops the batch; later rows are not sent first. `attempts + 1` and a sanitised `last_error` (no payload, at most 1,000 chars) are stored.
- AC-3 Backoff 1 s doubling to 60 s. After 10 attempts, or at once on a non-retryable error, the publisher stays on that row (retrying at the cap), logs `OutboxStuck` once per state change and the `outbox` health check is Degraded. The row is never sent to a DLQ. A later success clears the state.
- AC-4 Mark-published happens only after the broker ack (`acks=all`, idempotent producer). A crash between ack and mark gives a duplicate, which consumers dedupe on `eventId`.
- AC-5 Message: key `claimId` ("D", lowercase); value = `envelope::text` untouched; headers `X-Correlation-Id` (from the envelope), `event-id`, `event-type`, `event-version`.
- AC-6 Cancellation stops the loop; an already acknowledged row is still marked within a short grace; producer is flushed and disposed.

## 3. Design
- `OutboxPublisher : BackgroundService`. Per cycle: new DI scope, `ICommandTransaction.BeginAsync`, `FetchUnpublishedBatchAsync(BatchSize)`, send each row sequentially, `MarkPublishedAsync` after each ack, on first failure `RecordFailureAsync` and stop, commit once (marks + failure together). Rows stay locked (`SKIP LOCKED`) only for that short transaction. One instance per database (ARCHITECTURE s5 rule 3).
- `IOutboxSender` (test seam; Kafka-free publisher) with `KafkaOutboxSender` (singleton producer). It throws `OutboxSendException(isRetryable, sanitisedError)`; non-retryable: key/value serialisation, `MsgSizeTooLarge`, unknown topic or partition, topic/cluster authorisation failed, malformed envelope (no `correlationId`).
- Delay: after failure `min(1 s * 2^(attempts-1), 60 s)` from the persisted attempt count (survives restart); empty batch waits `PollInterval` (1 s); a full batch loops at once. Store/DB errors (`DbException`) log a warning and wait the cap; any other exception stops the host (fail loud, compose restarts it).
- `OutboxPublisherState` (singleton) holds the stuck row (eventId, sequence, attempts); `OutboxHealthCheck` returns Degraded when set (HTTP 200, so the container stays healthy). Logs carry eventId, sequence, attempts, error class only.
- Options (env-bound, validated on start): `Kafka:BootstrapServers` (required), `Kafka:MessageTimeout` 10 s, `Kafka:RequestTimeout` 5 s; `Outbox:Enabled` true, `BatchSize` 100, `PollInterval` 1 s, `MaxAttempts` 10, `BackoffInitial` 1 s, `BackoffCap` 60 s. Host tests without Kafka set `Outbox__Enabled=false`.
- Producer: `Acks=All`, `EnableIdempotence=true`, `MessageTimeoutMs`, `RequestTimeoutMs` bounded, `ClientId=claims-outbox`, PLAINTEXT in dev (production gap: TLS/SASL/ACLs, listed in README).

## 4. Data, events, trail, security
No schema change (uses `outbox` columns, SELECT+UPDATE). Publishes the 11 Phase 1 events; consumes none. No trail entries. Security: no secrets in code (bootstrap address from env); Kafka not published to the host, `data` network only; payload and PII never logged; plaintext broker is a known dev-only gap.

## 5. Compose
- `kafka`: `confluentinc/cp-kafka:7.6.0` pinned by digest, KRaft single node, auto-create topics off, replication factor 1 (so `acks=all` equals leader ack: honest limit), `data` network, no ports, `kafka-data` volume, healthcheck `kafka-topics --list`, `no-new-privileges`, caps dropped, heap capped.
- `kafka-init`: same image, one-shot, `kafka-topics --create --if-not-exists`: `claims.events` 3 partitions, `claims.events.dlq` 1 partition. Names duplicated from `Contracts.Topics` (compose cannot import them); a later compose contract test should assert they match.
- `claims`: `depends_on kafka-init: service_completed_successfully`, env `Kafka__BootstrapServers=kafka:9092`.

## 6. Verification by hand (no test suite)
1. `docker compose up --build -d`; `docker compose ps` shows kafka healthy, kafka-init exited 0, claims healthy.
2. `docker compose exec kafka kafka-topics --bootstrap-server localhost:9092 --describe` lists both topics (3 and 1 partitions).
3. Backlog flushed: `docker compose exec postgres psql -U postgres -d claims_db -c "select count(*) total, count(published_at) published, max(attempts) from claims.outbox"` gives published = total (101 for the seed), max attempts 0.
4. Sequence order: `... -c "select count(*) from (select published_at < lag(published_at) over (order by sequence) bad from claims.outbox) s where bad"` gives 0.
5. Consume: `docker compose exec kafka kafka-console-consumer --bootstrap-server localhost:9092 --topic claims.events --from-beginning --timeout-ms 15000 --property print.key=true --property print.headers=true > consumed.tsv` has 101 lines; each line shows headers `X-Correlation-Id`, `event-id`, `event-type`, `event-version` and key = claim id.
6. Per-claim order: extract `claimId eventId` from consumed.tsv (key, header) and from `select claim_id, event_id from claims.outbox order by sequence`; `LC_ALL=C sort -s -k1,1` both; `diff` is empty.
7. Stuck drill: `docker compose stop kafka`; `... -c "update claims.outbox set published_at=null where sequence > (select max(sequence)-5 from claims.outbox)"`; claims logs show failures and, after the 10th attempt, `OutboxStuck`; `attempts` and `last_error` set; `docker compose start kafka` drains the rows.

## 7. Risks and minimal cut
Risks: cp-kafka image under `read_only` (not applied; entrypoint writes config); librdkafka native lib in the aspnet bookworm image (PLAINTEXT needs nothing extra); head-of-line blocking (accepted, s12); single partition-leader durability. **Cut line:** keep publisher, producer, `Topics`/headers, kafka, kafka-init, Claims wiring; health check and backoff polish go last; kafka-ui not built.

## 8. Open questions (architect)
Partition counts (3 and 1) and `Outbox:Enabled` flag; whether the compose-to-`Topics` match needs a contract test now.

## 9. Review record
| Reviewer | Date | Verdict | Notes |
|---|---|---|---|
| architect | pending | | code first, review after |
