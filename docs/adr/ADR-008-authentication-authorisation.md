# ADR-008: Authentication and authorisation

Status: Accepted. Owner: architect.

## Context
Three roles (Claimant, Officer, Manager), a mock identity provider, and rules that depend on the user, not only the role (own claim, assigned officer, not the maker). Finance adds bank details and money endpoints that must never be reachable by the wrong caller.

## Decision
- **Identity** issues short-lived (30 min) JWTs signed RS256 with a dev key pair mounted from a file; services hold only the public key. Claims in token: `sub` (userId), `role` (array, may contain Officer and Manager), `name`, `market`. Seeded users include one dual-role user to prove maker-checker.
- **Gateway validates** signature, issuer, audience, expiry and routes; **every service validates again** (defence in depth); the Gateway is not a trust boundary on its own.
- **Authorisation:** named policies per endpoint, default deny (fallback policy requires authentication, endpoints without a policy fail a startup test). Ownership and rule checks happen in the application/domain layer using the token's `sub`, never request-body identity.
- **Service-to-service:** the only internal REST (reserve reconciliation) uses a token with role `Service`, issued by Identity for a fixed client secret from environment, on routes not mapped in the Gateway.
- **Sensitive data:** bank details exist only in Finance, masked (`accountLast4`) in all responses, trail entries, events and logs.
- Simulation controls exist only when `ASPNETCORE_ENVIRONMENT=Development`.

### Amendment 2026-10-28 (identity-gateway spec review, Q3/Q4/Q5)
- **Token parameters:** issuer `chubb-identity`, one audience `chubb-claims-api` accepted by the Gateway and every service; `ValidAlgorithms = [RS256]`, clock skew 30 s, `MapInboundClaims = false`, role claim type `role`. Values are configuration, not code. Consumers must accept `role` as a string or an array (a single-role token may serialise it as a string).
- **Dev key source:** a one-shot compose init container (`jwt-keys`) generates a 2048-bit RSA pair with `openssl` only if absent, into two named volumes: `jwt-private` (mounted read-only into Identity only) and `jwt-public` (mounted read-only into Gateway, Claims, Reporting, Notification). Nothing is committed; no gitleaks allowlist. Running without Docker uses `scripts/dev-keys.sh` writing to a git-ignored `.dev-keys/`. Tests generate keys in the fixture.
- **Seeded user ids:** fixed GUIDs as constants in `Contracts` (`Contracts.DevSeed.SeedUserIds`), ids only (no emails, names or passwords), so Claims can seed policies owned by the seeded claimants without calling Identity. Dev-only by name; in production `sub` comes from the IdP.
- Rejected for keys: committed dev keys (trips the secret scan, normalises allowlisting key material, same key on every clone); Identity generating keys at startup and publishing JWKS (closest to production, but adds a discovery endpoint, startup ordering and key fetch in four services). Rejected for audience: one audience per service (needs multi-`aud` tokens or token exchange because the Gateway forwards one token unchanged; documented as the production path). Rejected for ids: Claims looking up users through Identity REST (runtime coupling for seed data) or duplicated literals (silent drift).

## Alternatives considered
- **HS256 shared secret:** simplest, but every service could mint tokens. Rejected for RS256 (cost is a few lines).
- **Real OIDC provider (Keycloak):** correct for production, a heavy container for the cap. Documented as the production path.
- **Trust the Gateway only:** one misrouted port exposes everything. Rejected.
- **Roles only, no policy-resource checks:** fails BOLA and maker-checker. Rejected.

## Consequences
- Tokens cannot be revoked within 30 minutes (production gap).
- Dual-role accounts make role checks insufficient on purpose; the matrix in SECURITY-REQUIREMENTS lists the extra rule per endpoint.
- If time runs out, fall back to HS256 and say so in the walkthrough.
