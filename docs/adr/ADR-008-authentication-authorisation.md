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

## Alternatives considered
- **HS256 shared secret:** simplest, but every service could mint tokens. Rejected for RS256 (cost is a few lines).
- **Real OIDC provider (Keycloak):** correct for production, a heavy container for the cap. Documented as the production path.
- **Trust the Gateway only:** one misrouted port exposes everything. Rejected.
- **Roles only, no policy-resource checks:** fails BOLA and maker-checker. Rejected.

## Consequences
- Tokens cannot be revoked within 30 minutes (production gap).
- Dual-role accounts make role checks insufficient on purpose; the matrix in SECURITY-REQUIREMENTS lists the extra rule per endpoint.
- If time runs out, fall back to HS256 and say so in the walkthrough.
