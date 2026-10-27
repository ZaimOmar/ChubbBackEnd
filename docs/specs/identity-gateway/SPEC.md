# SPEC: Solution skeleton + Identity + Gateway  (service: Identity, Gateway)

Slice 1. Covers TASKS section 1 (scaffold) and section 3 (Identity and Gateway). No code exists yet.

| Field | Value |
|---|---|
| Author | backend-developer |
| Status | IMPLEMENTED (REVIEWED by architect 2026-10-28; deviations in section 14 await architect re-confirmation) |
| Phase | P1 build |
| Stories (US-xx) | US-23 (role-based mock login), US-20 (server-side actor and visibility: token is the only identity source), US-25 (run it locally: scaffold, seeded users, OpenAPI client source) |
| Business rules (BR-xx) | BR-02 (who acts, wrong role gets 403), BR-32 (dual-role user, maker is never checker), BR-11 (actor and role from the token), BR-78 (a claimant never holds a staff role; Officer plus Manager allowed) |
| Tasks (docs/planning/TASKS.md) | Section 1 "Solution scaffold"; section 3 "Identity", "Gateway (YARP)", "Aggregated OpenAPI"; SHOULD: correlationId, health endpoints, Swagger UI. Section 2: named policy on every endpoint, default deny |
| Related ADRs | ADR-001 (service split), ADR-003 (contract-first OpenAPI), ADR-008 (RS256 JWT, validate twice, default deny) |

## 1. Purpose and scope
Create the solution skeleton, then the two thin edge services. **Identity** owns mock users and roles and issues 30-minute RS256 dev JWTs. **Gateway** (YARP) is the single entry point: it validates every token (signature, issuer, audience, expiry, algorithm), applies CORS, propagates a correlation id, routes to the services, and publishes one aggregated OpenAPI document for the Angular client generator. Every downstream service validates the token again (ADR-008); the Gateway is not a trust boundary on its own.

**In scope:** solution and build setup (central packages, analysers, `.editorconfig`, warnings as errors); `Contracts` project with constants only (role names, claim names, market codes, correlation header name, `DevSeed.SeedUserIds`); Identity service (users, roles, seed, `POST /auth/token`, health); Gateway (routes, JWT validation, CORS, correlation id, aggregated OpenAPI, health); Identity `identity_db` and its Dockerfile; Gateway Dockerfile; compose entries for `postgres` (identity_db), `identity`, `gateway` (devops-engineer, see section 9); tests.

**Out of scope / not now:** refresh tokens, logout or revocation (production gap, ADR-008); registration, password reset, user admin; the `Service` role token and internal routes (Phase 2 reconciliation); Finance routes `/payments`, `/funds`, `/ledger` (kept only as a commented-out block in the route config, TASKS COULD P2); rate limiting and TLS (production gaps); role-based authorisation at the Gateway beyond "authenticated" (roles and ownership are enforced in each service); Claims, Reporting and Notification behaviour (their own specs).

## 2. Acceptance criteria
- **AC-1 (US-23)** Given a seeded user and the right password, when `POST /auth/token`, then 200 and an RS256 JWT with `sub`, `name`, `market`, `role` (array), `iss`, `aud`, `iat`, `exp` (30 minutes), `jti`.
- **AC-2 (US-23)** Given a wrong password or unknown email, then 401 `invalid-credentials` with the same body for both (no user enumeration); for an unknown email the password is still verified against a dummy hash so response timing does not reveal whether the user exists. Given a malformed or oversized body, then 400/413 problem details.
- **AC-3 (US-23, BR-32)** Given the seeded dual-role user, then the token `role` array contains both `Officer` and `Manager`; single-role users contain exactly their one role.
- **AC-4 (US-23, BR-02)** Given a valid token, when it is used on a route whose service policy requires another role, then the service returns 403 (the Gateway forwards the token unchanged; the role gate lives in the service). Verified in this slice by a stub downstream that enforces a Manager-only policy on `/dashboard/**` (`Gateway_ServiceRoleRefusal_PassesThrough403Untouched`); end to end once Claims exists.
- **AC-5 (US-20, S1)** Given no token, a malformed token, an expired token, a wrong issuer, a wrong audience, a token signed with another key, an `alg=none` token or an HS256 token signed with the public key as secret, when any routed endpoint except `POST /auth/token` and health is called through the Gateway, then 401 problem details and nothing is forwarded.
- **AC-6 (US-20)** Given a request with a spoofed identity header (e.g. `X-User-Id`, `X-User-Role`) or a body `actorId`, then the Gateway neither trusts nor forwards identity headers; identity is only the validated token.
- **AC-7 (US-23)** Given a browser origin that is the configured frontend origin, then preflight succeeds with credentials-free CORS headers; given any other origin, then no CORS headers are returned.
- **AC-8 (US-25, ADR-003)** Given the stack is running, when `GET /openapi/v1.json` on the Gateway, then one OpenAPI 3 document built by merging the committed `contracts/openapi/*.yaml` files (read from `OpenApi:ContractsPath`, no call to any downstream service), containing every service's operations under the paths the client calls (the Gateway paths), with the bearer security scheme, and excluding `/internal/**` and health routes. The result does not depend on which services are up. A path or schema-name collision between files fails a Gateway test.
- **AC-8b (ADR-003)** Given the Gateway runs in any environment other than Development, then `/openapi/v1.json` and `/swagger` are not served (404).
- **AC-9 (US-25)** Given `docker compose up`, then Postgres, Identity and Gateway start with zero manual steps, migrations and the seed apply on startup, and the documented seeded credentials work.
- **AC-10 (security gate)** Given the Identity and Gateway endpoint tables, then every endpoint has a named policy or an explicit `AllowAnonymous` (only `POST /auth/token` and health), and a startup test fails if one does not.
- **AC-11** Given any request, then the response carries `X-Correlation-Id` (the inbound value if it is a valid GUID, otherwise a new one), the same value is forwarded downstream and is on every log line.

## 3. Domain model and invariants
Identity is deliberately thin; the only rules are on the user aggregate.
- `User`: `Id` (Guid, stable seeded value), `Email` (unique, case-insensitive), `DisplayName`, `Market`, `PasswordHash`, `Roles` (set of `Role`).
- `Role`: `Claimant | Officer | Manager` (`Service` is Phase 2, not created).
- Invariants (Domain layer, unit tested): a user has at least one role; roles are distinct; a user with `Claimant` and a staff role is rejected (BR-78: a claimant never holds a staff role, which keeps ownership rules unambiguous); `Officer` and `Manager` together is allowed on purpose (BR-78, BR-32); market is one of SG, HK, MY, TH, ID, AU; display name and email are non-empty.
- Credential check (`User.VerifyPassword` via an `IPasswordVerifier` port) is in Application; the token is built from the user in an `ITokenIssuer` (Infrastructure). Role and claim names come from `Contracts`.
- Token lifetime is configuration (`Jwt:LifetimeMinutes`, default 30). Time via `TimeProvider`; ids via the seeded constants (no `Guid.NewGuid()` in domain logic).

## 4. API (REST via Gateway)
### 4.1 Identity (route `/auth/**` through the Gateway)
| Method + route | Caller (auth policy) | Request | Success | Errors |
|---|---|---|---|---|
| `POST /auth/token` | anonymous (`AllowAnonymous`, the only one) | `{ "email": string, "password": string }` | 200 `{ "accessToken": string, "tokenType": "Bearer", "expiresIn": 1800 }` | 400 `validation-error` (missing, over-length, bad email format); 401 `invalid-credentials`; 413 body over 4 KB |
| `GET /health/live`, `GET /health/ready` | anonymous (`AllowAnonymous`, not routed by the Gateway) | none | 200 | 503 when `identity_db` is unreachable (ready only) |

Notes: no `GET /auth/me`; the frontend decodes the token claims (Q6 closed: not needed). Unknown-email logins run the password check against a dummy hash (CR-3). Request DTO has only `email` and `password` (no role or market is client-settable). Login failures are logged with a hashed or omitted identifier, never the email or password. Idempotency and concurrency: n/a (no state changes after seed).

### 4.2 Gateway
| Route (Gateway path) | Cluster (destination) | Policy at the Gateway | Notes |
|---|---|---|---|
| `POST /auth/token` (exact route, method-restricted) | Identity | anonymous | The only anonymous proxied route; no `/auth/{**}` catch-all, so any other `/auth` path or method returns 404 from the Gateway |
| `/claims/{**}` | Claims | `authenticated` | All Claims routes from the matrix incl. timeline, assign, decision-proposals; roles enforced by Claims |
| `/dashboard/{**}`, `/staff/{**}` | Reporting | `authenticated` | Manager-only enforced by Reporting; Reporting paths (Q7, A7): `GET /dashboard/outstanding`, `/dashboard/exposure`, `/dashboard/pending-approvals` (checker queue), `/staff/workload`, SSE `/dashboard/stream` if built; all Manager-only; routes stay prefix routes |
| `/notifications/{**}` | Notification | `authenticated` | `GET /notifications/mine` (own only, enforced by Notification); prefix route |
| `/openapi/v1.json`, `/swagger` | served by Gateway, Development only | anonymous | Aggregated contract (design in 4.3); not mapped in other environments |
| `/health/live`, `/health/ready` | served by Gateway | anonymous | Ready checks downstream cluster reachability is NOT included (Gateway stays up if a service is down) |
| `/internal/**`, `/dev/**` | none | n/a | Deliberately unmapped: 404 (S28). Finance routes `/payments`, `/funds`, `/ledger` present only as a commented-out block in `appsettings` (TASKS COULD P2) |

Gateway behaviour (YARP, config-driven `ReverseProxy` section, destinations from environment, no hard-coded hosts):
- Fallback authorisation policy requires an authenticated user; only the routes marked anonymous opt out. Default deny.
- JWT bearer validation: issuer, audience, lifetime (clock skew 30 s), signing key from the public key file, `ValidAlgorithms = RS256` only, `RequireSignedTokens`, `MapInboundClaims = false`, role claim type `role`.
- The `Authorization` header is forwarded unchanged so each service revalidates. Inbound `X-User-*`/identity-like headers are stripped by a request transform (AC-6).
- CORS: one named policy, allowed origins from `Cors:AllowedOrigins` (dev default `http://localhost:4200`), explicit methods and headers (`Authorization`, `Content-Type`, `Idempotency-Key`, `X-Correlation-Id`), no wildcard, no credentials, exposes `X-Correlation-Id`.
- Request body limit 1 MB on proxied routes (S7), 4 KB on `/auth/token`; request timeout set on the cluster.
- Errors: Gateway-generated 401/403/404/413/502/504 are RFC 7807 problem details with stable `type` codes (`unauthenticated`, `upstream-unavailable`, ...), no stack traces or upstream detail. Downstream problem details pass through untouched.

### 4.3 Aggregated OpenAPI design
- Source of truth: the committed `contracts/openapi/<service>.yaml` files (ADR-003, amended). They are copied into the Gateway image and read from `OpenApi:ContractsPath`.
- `GET /openapi/v1.json` merges `paths` and `components.schemas` of those files into one document, prefixes nothing (service paths already equal the Gateway paths), adds `components.securitySchemes.bearer` and a global security requirement (except `/auth/token`), and removes `/internal/**` and `/health/**`. No outbound fetch, cache or timeout logic; the document is the same whichever services are running.
- A path collision or schema-name collision across files fails a Gateway test (schema names are prefixed by the owning service in its own spec).
- `/openapi/v1.json` and `/swagger` are served only in Development (AC-8b). Swagger UI on the Gateway points at the merged document (SHOULD). The Angular client is generated from `/openapi/v1.json`; README documents the command.
- Drift check (ADR-003): a contract test regenerates Identity's spec from the running code and compares it to `contracts/openapi/identity.yaml`, so committed file equals running code.
- This slice: only `identity.yaml` exists; the other services' files are added by their own slices.

## 5. Data
Database `identity_db` (Postgres 16, own instance database, accessed only by Identity). EF Core with `IEntityTypeConfiguration<T>`, explicit mappings, one reviewed migration with a reversible `Down`; auto-applied on startup in Development only.
| Table | Columns | Constraints and indexes |
|---|---|---|
| `users` | `id uuid PK`, `email varchar(254) not null`, `display_name varchar(100) not null`, `market char(2) not null`, `password_hash varchar(200) not null`, `created_at timestamptz not null` | unique index on `lower(email)`; check `market in ('SG','HK','MY','TH','ID','AU')` |
| `user_roles` | `user_id uuid FK`, `role varchar(20) not null` | PK (`user_id`, `role`); check `role in ('Claimant','Officer','Manager')` |
No `rowVersion`: rows are never updated after seed. No money columns. Passwords are stored only as ASP.NET Core `PasswordHasher` hashes (PBKDF2).

**Seed (idempotent, runs on startup):** user ids come from `Contracts.DevSeed.SeedUserIds` (fixed GUIDs only, no email, name or password; A5) so Claims can seed policy ownership with the same `sub`. All users share one dev password read from `Identity:SeedPassword` (env `IDENTITY__SEEDPASSWORD`, dev-only value in `.env.example`, never in code or compose). Documented in README (US-25).
| Email | Display name | Roles | Market | Purpose |
|---|---|---|---|---|
| `claimant@chubb.test` | Alex Claimant | Claimant | SG | Claimant journeys; owns seeded policies |
| `officer@chubb.test` | Olivia Officer | Officer | SG | Maker |
| `manager@chubb.test` | Marcus Manager | Manager | SG | Checker |
| `officer.manager@chubb.test` | Dana Dual | Officer, Manager | SG | Dual-role: proposes then is refused on confirm (BR-32, S11) |
| `claimant2@chubb.test` | Bao Claimant | Claimant | HK | BOLA test S2 (claimant B) |
| `officer2@chubb.test` | Omar Officer | Officer | SG | S4 and S10 (two officers) |
The last two rows go beyond the four users named in the slice brief; accepted (Q2). Six seeded users in total.

## 6. Events
n/a. Identity and Gateway publish and consume no Kafka events (CLAUDE.md: only Claims has an outbox). Correlation id travels on HTTP only in this slice (`X-Correlation-Id`); outbox and Kafka headers are the messaging slice.

## 7. Claim progress trail
n/a. Neither service writes trail entries. Contribution to the trail: tokens carry `sub` and `role`, which Claims uses as `actorId` and `actorRole` (US-20, BR-11); the Gateway never injects identity. Refused attempts on Claims are trailed by Claims, not here. Failed logins are logged (no personal data), not trailed.

## 8. Security
**Authorisation matrix rows implemented** (SECURITY-REQUIREMENTS section 3): "Unauthenticated: 401 on everything except `POST /auth/token`" (Gateway fallback policy plus per-service validation); principle 1 (default deny, startup test for endpoints without policy); principle 3 (identity from the token only). The per-endpoint role rows for Claims, Reporting and Notification are implemented in those services; this slice supplies the role claims and the policy name constants they use (`Claimant`, `Officer`, `Manager` in `Contracts`).

**Token design (ADR-008):** RS256, keys are generated once by the one-shot compose init container `jwt-keys` (openssl, 2048-bit, only if absent) into two volumes: `jwt-private` (read-only, mounted by Identity only, `Jwt:PrivateKeyPath`) and `jwt-public` (read-only, mounted by Gateway, Claims, Reporting and Notification, `Jwt:PublicKeyPath`). Nothing is committed and no secret-scan allowlist is needed. Without Docker, `scripts/dev-keys.sh` writes to git-ignored `.dev-keys/`; tests generate keys in the fixture. Issuer `chubb-identity`, audience `chubb-claims-api` (configuration, Q4). Claims: `sub`, `role` (JSON array), `name`, `market`, `jti`. 30-minute lifetime. Every consumer (Gateway and services) accepts `role` as either a string or an array.

**Threats mitigated** (section 2): Spoofing (forged, expired, wrong-key, alg-confusion tokens), Tampering (no client-settable identity, role or market in any DTO), Information disclosure (uniform login failure, problem details only, no stack traces, hashes never returned), Denial of service (body limits, bounded fields), Elevation of privilege (default deny, `alg` pinned, no identity headers trusted).

**Security tests:** S1 (all variants, AC-5), S6 (problem details for validation, 401, 404, 500 from Gateway and Identity; no stack trace), S7 (oversized body, over-length fields), S28 partial (`/internal/**` gives 404 at the Gateway; the 403-with-user-token half is Phase 2), plus S3 and S5 groundwork (role claims present; identity headers stripped). Remaining S-ids belong to Claims.

**Never logged or returned:** passwords, password hashes, tokens, the private key, email addresses in failure logs, the seed password. Serilog request logging excludes the `Authorization` header and request bodies for `/auth/token`. Logs carry `correlationId` and `userId` (`sub`) only.

## 9. Dependencies and sequencing
- Needs first: nothing in code. Architect decisions (A1, A3, A4, A5, A7, A8) and the Q2, Q6, Q9 answers are recorded in section 12.
- `devops-engineer` owns Dockerfiles, `docker-compose.yml`, `.env.example` and the dev key generation/mount; this spec states only what Identity and Gateway need (env keys below). `scripts/security-check.sh` runs at the end of the slice.
- Unblocks: Claims (roles, `sub`, public key, policy constants, seeded user ids for policies), Reporting, Notification (same JWT config), the frontend (login, generated client).
- Minimal hosts (A8): Claims, Reporting and Notification are created in step 1 with health endpoints, JWT validation (same config keys) and the fallback authenticated policy, so they are default-deny from the first commit and covered by AC-10. No database and no compose entry until their own slice; until then the Gateway returns 502/503 `upstream-unavailable` problem details for their routes.

## 10. Test plan
| Criterion / risk | Test type | Test name (`Method_Scenario_Expected`) |
|---|---|---|
| Invariant: at least one role | unit | `User_Create_WithNoRoles_IsRejected` |
| Invariant: claimant cannot also be staff | unit | `User_Create_WithClaimantAndOfficer_IsRejected` |
| BR-32: Officer plus Manager allowed | unit | `User_Create_WithOfficerAndManager_Succeeds` |
| Invariant: market is one of six | unit | `User_Create_WithUnknownMarket_IsRejected` |
| AC-1 claims and lifetime | unit | `TokenIssuer_Issue_ContainsExpectedClaimsAndThirtyMinuteExpiry` |
| AC-3 dual role | unit | `TokenIssuer_Issue_ForDualRoleUser_HasBothRoleClaims` |
| AC-1 RS256 verifiable with public key only | unit | `TokenIssuer_Issue_IsVerifiableWithPublicKeyOnly` |
| Password handling | unit | `PasswordVerifier_Verify_WrongPassword_ReturnsFalse` |
| AC-2 validation | unit | `LoginValidator_Validate_EmptyOrOverlongFields_Fails` |
| AC-1 login success | integration (Testcontainers Postgres) | `PostToken_ValidCredentials_ReturnsBearerToken` |
| AC-2 uniform failure | integration | `PostToken_WrongPasswordOrUnknownEmail_Returns401WithIdenticalBody` (also asserts the dummy-hash path runs for unknown email) |
| AC-2, S7 | integration | `PostToken_OversizedBody_Returns413ProblemDetails` |
| AC-3 seed | integration | `Seed_OnStartup_CreatesSixUsersWithExpectedRoles` |
| Seed idempotent | integration | `Seed_RunTwice_DoesNotDuplicateUsers` |
| Migration up/down | integration | `Migrations_ApplyAndRevert_Succeed` |
| AC-5, S1 | integration (Gateway test host + stub downstream) | `Gateway_ProtectedRoute_NoToken_Returns401`; `..._ExpiredToken_Returns401`; `..._WrongIssuer_...`; `..._WrongAudience_...`; `..._WrongSigningKey_...`; `..._AlgNone_...`; `..._Hs256SignedWithPublicKey_...` |
| AC-4 token forwarded, role gate downstream | integration | `Gateway_ValidToken_ForwardsAuthorizationHeaderUnchanged` |
| AC-6 spoofed headers | integration | `Gateway_SpoofedIdentityHeaders_AreNotForwarded` |
| AC-7 CORS | integration | `Gateway_Preflight_AllowedOrigin_ReturnsCorsHeaders`; `Gateway_Preflight_OtherOrigin_ReturnsNoCorsHeaders` |
| AC-8 aggregation | integration | `OpenApi_Aggregate_MergesCommittedFilesAndRemovesInternalPaths`; `OpenApi_Aggregate_UsesCommittedFilesWithoutDownstreamCalls` |
| AC-8 collisions | integration | `OpenApi_Aggregate_DuplicateSchemaNames_Fails`; `OpenApi_Aggregate_DuplicatePaths_Fails` |
| AC-8b Development only | integration | `OpenApi_Development_EndpointServed`; `OpenApi_NonDevelopment_EndpointAbsent` (also `/swagger`) |
| CR-2 exact auth route | integration | `Gateway_OtherAuthPaths_Return404` |
| ADR-003 drift | contract | `IdentityOpenApi_RegeneratedSpec_MatchesCommittedContract` |
| AC-10 default deny | integration | `Endpoints_AllRoutes_HaveNamedPolicyOrExplicitAnonymous` (Identity and Gateway) |
| S28 groundwork | integration | `Gateway_InternalAndDevRoutes_Return404` |
| S6 | integration | `Gateway_UnmappedRouteAndUpstreamFailure_ReturnProblemDetailsWithoutInternals` |
| AC-11 | integration | `Gateway_Request_EchoesOrGeneratesCorrelationIdAndForwardsIt` |
| Log leak | integration | `Login_Failure_LogsContainNoEmailPasswordOrToken` |
Tests are deterministic: injected `TimeProvider`, test RSA key pairs generated in the fixture, no sleeps.

## 11. Implementation plan
Each step is one small conventional commit, domain first, tests with the code.
1. `chore:` solution skeleton: `ChubbBackEnd.sln`, `Directory.Build.props` (nullable, warnings as errors, analysers), `Directory.Packages.props` (pinned), `.editorconfig`, `global.json`; projects `src/Contracts`, `src/Gateway`, `src/Identity`, `src/Claims`, `src/Reporting`, `src/Notification` (minimal hosts), `tests/*`. No Finance project.
2. `feat:` Contracts constants (roles, claim names, markets, `X-Correlation-Id`, `DevSeed.SeedUserIds`).
3. `feat:` Identity Domain (`User`, `Role`, invariants) with unit tests.
4. `feat:` Identity Application (login handler, validator, ports) and Infrastructure (EF mappings, migration, password hasher, RS256 token issuer, seeder) with tests.
5. `feat:` Identity Api (`POST /auth/token`, problem details, health, Serilog with correlationId, Swagger) and `contracts/openapi/identity.yaml` with drift test.
6. `feat:` Gateway (YARP routes, JWT validation, fallback policy, CORS, header strip, body limits, problem details, correlation id) with S1 tests.
7. `feat:` Gateway OpenAPI aggregation from committed contract files (Development only) and Swagger UI with tests.
8. `chore:` Dockerfiles, compose entries (incl. `jwt-keys` init container and `jwt-private`/`jwt-public` volumes), `.env.example` (devops-engineer); README section on seeded users and client generation.
9. Run `dotnet format --verify-no-changes`, `dotnet build`, `dotnet test`, `scripts/security-check.sh`; ask `code-reviewer` and `security-reviewer`.

Configuration keys: `ConnectionStrings:IdentityDb`, `Identity:SeedPassword`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:LifetimeMinutes`, `Jwt:PrivateKeyPath` (Identity only), `Jwt:PublicKeyPath` (Gateway and all services), `Cors:AllowedOrigins`, `ReverseProxy:*` (clusters from env), `OpenApi:ContractsPath`. All bound with validated options at startup.

Estimate (CR-7): about 60-75 minutes for skeleton, Identity, Gateway, aggregation and roughly 30 tests (ARCHITECTURE section 11 row 0 shows 0:30, which is optimistic; `product-owner` to decide). Cut order if short: Swagger UI first, then the extra aggregation tests, then the tests for the two extra seed users. Falling back from RS256 is allowed only with architect approval (ADR-008).

## 12. Risks, assumptions, open questions
**Assumptions:** `POST /auth/token` takes email plus password (US-23 "valid seeded credentials"; the security matrix names this route); one shared dev password from environment; single audience for all services; seeded user ids are fixed GUIDs; Gateway does authentication only and services do role and ownership checks.

**Risks:** aggregated spec can drift or collide if services reuse schema names; if the Gateway were reachable but a service port is exposed, only the service's own validation protects it (hence double validation); no revocation within 30 minutes (documented production gap).

**Open questions:** all closed below (Q1, Q3, Q4, Q5, Q7, Q8 by the architect; Q2, Q6, Q9 by the owners).
- Q2 (`product-owner`) CLOSED: accepted. Seed `claimant2@chubb.test` and `officer2@chubb.test`; six seeded users in total.
- Q6 (`product-owner`) CLOSED: no `GET /auth/me`; the frontend decodes the token.
- Q9 (`business-analyst`) CLOSED: BR-78 (a claimant never holds a staff role; Officer plus Manager allowed) is cited in the invariant and the header.

**Architect answers (2026-10-28).** Recorded in ADR-003 (amended), ADR-008 (amendment), ARCHITECTURE sections 1, 2, 10 and SECURITY-REQUIREMENTS sections 2, 3, 6.
- **A1 (Q1): TASKS wins; ADR-003 amended.** The Gateway serves one merged `GET /openapi/v1.json` and the Angular client is generated from it. Per-service `contracts/openapi/<service>.yaml` stay the source of truth. Change from this draft: the merge input is the **committed contract files copied into the Gateway image**, not the live downstream `/swagger` documents. Why: the generated client must not depend on which services are up; no outbound fetch, cache or timeout logic in the Gateway; the frontend can generate before a service exists; the drift test already guarantees committed file = running code. Schema-name or path collisions fail a Gateway test. `/openapi/v1.json` and `/swagger` are served only in Development.
- **A3 (Q3): generated, nothing committed.** A one-shot compose init container `jwt-keys` (openssl) creates a 2048-bit RSA pair only if absent, into volume `jwt-private` (read-only, Identity only) and volume `jwt-public` (read-only, Gateway, Claims, Reporting, Notification). No gitleaks allowlist. Without Docker: `scripts/dev-keys.sh` into git-ignored `.dev-keys/`. Tests generate keys in the fixture. Rejected: committed dev keys (secret scan, same key on every clone); Identity-hosted JWKS (more moving parts in four services; production path). devops-engineer owns the compose part.
- **A4 (Q4): confirmed.** Issuer `chubb-identity`, one audience `chubb-claims-api` for the Gateway and every service, as configuration. Per-service audiences rejected: the Gateway forwards one token unchanged, so they would need multi-`aud` tokens or token exchange (production path). Consumers must accept `role` as a string or an array.
- **A5 (Q5): constants in `Contracts`.** `Contracts.DevSeed.SeedUserIds` holds fixed GUIDs only (no email, name or password). Identity seeds with them; Claims seeds policy ownership with them. Within the sharing rule (constants only). Rejected: Claims calling Identity for seed data (runtime coupling); duplicated literals (silent drift).
- **A7 (Q7): paths fixed.** Reporting: `GET /dashboard/outstanding`, `GET /dashboard/exposure`, `GET /dashboard/pending-approvals` (the checker queue), `GET /staff/workload`; SSE if built is `GET /dashboard/stream`. Notification: `GET /notifications/mine`. All Manager-only except `/notifications/mine` (own only). Gateway routes stay prefix routes `/dashboard/{**}`, `/staff/{**}` to Reporting and `/notifications/{**}` to Notification.
- **A8 (Q8): minimal hosts now.** Claims, Reporting and Notification are created in step 1 as hosts with health endpoints, JWT validation (same config keys) and the fallback authenticated policy, so they are default-deny from the first commit and covered by the AC-10 startup test. No database, no compose entry until their own slice; until then the Gateway returns 502/503 problem details `upstream-unavailable` for their routes. Rejected: creating them later (TASKS scaffold MUST lists them; Gateway route config and tests would need stub targets anyway).

## 13. Review record
| Reviewer | Date | Verdict (REVIEWED / CHANGES REQUESTED) | Notes |
|---|---|---|---|
| architect | 2026-10-28 | CHANGES REQUESTED | Design fits ADR-001/003/008, database-per-service and default deny; token validation, header stripping, CORS and S1 coverage are good. Small, specific changes before coding: **CR-1** (A1) rewrite AC-8 and 4.3 to merge the committed `contracts/openapi/*.yaml` files (config `OpenApi:ContractsPath`), remove `OpenApi:Sources`, the 60 s cache and "a service that is down is omitted"; add AC/test "Development only" (`OpenApi_NonDevelopment_EndpointAbsent`) and a path-collision test; replace `OpenApi_Aggregate_SourceDown_IsOmittedNotFailed` with `OpenApi_Aggregate_UsesCommittedFilesWithoutDownstreamCalls`. **CR-2** 4.2: replace anonymous `/auth/{**}` with an exact route `POST /auth/token` (no anonymous catch-all forwarding); add test `Gateway_OtherAuthPaths_Return404`. **CR-3** AC-2/4.1: unknown email must verify against a dummy hash so timing does not enumerate users; add it to the uniform-failure test. **CR-4** section 8: key mounting per A3 (two volumes, no secret-scan allowlist); state that `role` may be a string or an array. **CR-5** section 5 and step 2: seed ids come from `Contracts.DevSeed.SeedUserIds` (A5). **CR-6** sections 4.2 and 9: Reporting/Notification paths per A7; minimal hosts per A8 (JWT and fallback policy wired, health only, no DB or compose entry yet). **CR-7** estimate: 30 min is optimistic for skeleton + Identity + Gateway + aggregation + ~30 tests; state a realistic figure (about 60-75 min) and the cut order (Swagger UI, then extra aggregation tests) so `product-owner` can decide. Q2, Q6 (product-owner) and Q9 (business-analyst) still pending; their answers must be recorded before step 3. Re-review is a quick check of CR-1 to CR-7 only. |
| architect | 2026-10-28 | REVIEWED | Re-review of CR-1 to CR-7 and closed questions only. CR-1 applied (AC-8, 4.3: committed files via `OpenApi:ContractsPath`, no fetch/cache, collision and no-downstream tests, AC-8b). CR-2 applied (exact `POST /auth/token`, `Gateway_OtherAuthPaths_Return404`; matches SECURITY-REQUIREMENTS EoP row and matrix). CR-3 applied (AC-2, 4.1, test asserts dummy-hash path; matches Information-disclosure row). CR-4 applied (two volumes, no allowlist, `role` string or array; matches ADR-008). CR-5 applied (`Contracts.DevSeed.SeedUserIds`, ids only). CR-6 applied (A7 paths in 4.2, A8 minimal hosts in 9 and step 1). CR-7 applied (60-75 min, cut order, RS256 not cut); reflected in ARCHITECTURE section 11 row 0 with a note leaving the schedule/cut-line decision to `product-owner`. Q2 (six users, seed test updated), Q6 (no `/auth/me`) and Q9/BR-78 (invariant, header, unit test) recorded consistently. Extending AC-8b and `OpenApi_NonDevelopment_EndpointAbsent` to `/swagger` is correct: it matches ADR-003 (amended) and the SECURITY-REQUIREMENTS matrix row for `/openapi/v1.json`, `/swagger` (Development only). Non-blocking tidy-ups for the developer: section 12 Risks still says "a committed dev private key trips the secret scan (Q3)", which is obsolete after A3 (nothing committed); section 1 In scope list for `Contracts` should also name `DevSeed.SeedUserIds`. Coding may start. |
| architect | 2026-10-28 | REVIEWED (deviations 1-8 confirmed) | Re-confirmation of section 14 against ADR-003, ADR-008, ARCHITECTURE and SECURITY-REQUIREMENTS, checked against the code. **D1 confirmed:** `User.NormalizeEmail` (trim, lower-invariant) is used both on create and in `LoginHandler`, so a plain unique `ix_users_email` gives the same guarantee while the domain is the only writer; optional cheap backstop `CHECK (email = lower(email))` if a migration is touched again (not required). Section 5 table should say "unique index on `email` (stored lower-cased)". **D2 confirmed:** per-operation bearer on every non-`/auth/token` operation is semantically equal to a global requirement with an anonymous override; ADR-003's wording "global security requirement" is now loose and should be aligned by the architect (ADR-003 was not in this edit scope). **D3 confirmed:** route table now in ARCHITECTURE section 2a and SECURITY-REQUIREMENTS Finance section (Phase 2, designed not built, 404 in Phase 1); design note added: the Phase 2 `/claims/{id}/payee-details` route must be ordered before `/claims/{**}` and tested. Section 1 "Out of scope" and 4.2 still mention the commented-out block; developer to update the wording. **D4 confirmed:** merging only `paths` and `components.schemas` is what ADR-003 states; failing loudly on any other component kind is stricter and correct (no silently broken `$ref`). Consequence for the Claims/Reporting/Notification contract authors: use schema `$ref`s or inline responses/parameters, not `components.responses`/`parameters`. Whole-segment exclusion of `/health` and `/internal` is correct. **D5 confirmed:** required by the "no shared code beyond `Contracts`" rule (ADR-001); drift risk mitigated by requiring each service slice to carry its own S1 variants (recorded in SECURITY-REQUIREMENTS section 3). **D6 confirmed, stronger than the spec:** allowlist replaces the denylist; SECURITY-REQUIREMENTS Spoofing row and ARCHITECTURE 2a updated. Follow-up for the Claims spec: if optimistic concurrency uses `If-Match` from the browser, it is on the Gateway allowlist but not yet in the CORS allowed headers (`Authorization`, `Content-Type`, `Idempotency-Key`, `X-Correlation-Id`); add it there in that slice. **D7 confirmed:** fallback authenticated policy with no scheme fails closed (challenge throws, 500 problem details, nothing served) and the anonymous 404 catch-all keeps unmapped paths at 404; AC-10 test remains the primary guard. **D8 confirmed:** validated options on CORS origins and `OpenApi:ContractsPath`, plus merging at startup in Development, turn config and contract errors into startup failures, consistent with the "bound with validated options" rule. Also recorded: per-service compose secrets and the `edge`/`data` network split (ARCHITECTURE section 10, SECURITY-REQUIREMENTS sections 1, 2, 6). Header Status line can drop "deviations ... await architect re-confirmation". |

## 14. Implementation deviations (recorded 2026-10-28, for architect re-confirmation)
1. **Email uniqueness:** emails are lower-cased by `User.NormalizeEmail` (domain) and a plain unique index `ix_users_email` enforces uniqueness, instead of a `lower(email)` index. Same guarantee; the domain is the only writer.
2. **OpenAPI security:** the bearer requirement is added to each operation of the merged document (except `/auth/token`) instead of a global requirement, because the serializer drops an empty per-operation override.
3. **Finance routes:** no commented-out Finance route block in `appsettings` (JSON has no comments; commented-out code is banned). The Phase 2 routes are documented in ARCHITECTURE instead.
4. **Merge scope:** the aggregator merges `paths` and `components.schemas` only; a contract that defines any other component kind makes the merge fail with `OpenApiMergeException`. `/health` and `/internal` are excluded as whole path segments.
5. **Minimal hosts:** Claims, Reporting and Notification each carry their own copy of the small JWT/fallback-policy setup (no shared code beyond `Contracts`).
6. **Header handling:** the Gateway forwards only an allowlist of request headers (plus `X-Correlation-Id`), instead of stripping a list of identity-like headers.
7. **Identity default deny:** Identity also registers a fallback authenticated policy and an anonymous catch-all returning 404; with no authentication scheme, any future endpoint without an explicit policy fails closed.
8. **Startup checks:** CORS origins must be absolute http(s) origins without a path; `OpenApi:ContractsPath` must exist and the contracts are merged at startup in Development.
