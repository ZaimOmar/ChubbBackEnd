# Coding Guidelines – ChubbBackEnd (C# / .NET 8)

Binding for every coding agent (`backend-developer`, `messaging-engineer`, `qa-test-engineer`, `devops-engineer`) and checked by `code-reviewer` and `security-reviewer`. Goal: code a new engineer can read once and defend in the walkthrough. If a rule here conflicts with surrounding code, follow this file and flag it.

## 1. Principles
1. Correct first, then clear, then fast. Do not optimise without a measured reason.
2. Simple over clever. The smallest design that satisfies the story wins; no speculative abstractions, layers or patterns.
3. Make invalid states unrepresentable; enforce rules in the Domain layer.
4. Fail loudly and early with meaningful errors; never swallow exceptions.
5. Every change is small, tested and reviewable on its own.

## 2. Project setup (enforced)
- `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`, latest analysers.
- `.editorconfig` at the repo root; `dotnet format --verify-no-changes` must pass.
- Central package management (`Directory.Packages.props`); pinned versions; no unused packages.
- File-scoped namespaces. One public type per file, file named after the type.

## 3. Structure and layering (per service)
```
Domain/          entities, value objects, state machine, domain errors   (no dependencies)
Application/     use cases (commands, queries), validators, interfaces   (depends on Domain)
Infrastructure/  EF Core, Kafka, outbox, clock, external clients         (implements Application interfaces)
Api/             endpoints/controllers, auth policies, DTOs, DI wiring   (thin)
```
- Dependencies point inward. Domain references nothing; Api never touches EF directly.
- A service never references another service's projects. Only `Contracts` (events, constants) is shared.
- Do not create a layer, interface or abstraction until there is a second implementation or a test seam that needs it.

## 4. Naming
- Types and methods: `PascalCase`, intention-revealing (`ApproveClaim`, `ClaimStatusTransition`). Private fields `_camelCase`. Async methods end in `Async`.
- Booleans read as questions (`IsOpen`, `HasPendingInfoRequest`). Collections are plural nouns.
- Avoid `Manager`, `Helper`, `Util`, `Data`, `Info` as names. No abbreviations except well-known ones (`Id`, `Url`, `Dto`).
- Commands/queries: `ApproveClaimCommand`, `GetClaimQuery`; handlers: `ApproveClaimHandler`.

## 5. Methods and classes
- A method does one thing, about 20 lines max, at most 3 parameters (otherwise a request object). Early returns over nesting; nesting depth at most 3.
- A class has one reason to change. Prefer composition over inheritance. Prefer `sealed` classes, `record` for immutable DTOs/events, `readonly` where possible.
- No static mutable state. No service locator. Constructor injection only.
- Magic numbers and strings become named constants or configuration (topic names live in `Contracts`).

```csharp
// Good: rule lives in the domain, caller cannot bypass it
public sealed class Claim
{
    public ClaimStatus Status { get; private set; }

    public Result Approve(Money approvedAmount, OfficerId by, IClock clock)
    {
        if (Status is not ClaimStatus.UnderReview)
            return Result.Failure(ClaimErrors.InvalidTransition(Status, ClaimStatus.Approved));
        if (approvedAmount.Amount > Reserve.Amount)
            return Result.Failure(ClaimErrors.ApprovalExceedsReserve);

        Status = ClaimStatus.Approved;
        _history.Add(ClaimHistoryEntry.Approved(by, approvedAmount, clock.UtcNow));
        return Result.Success();
    }
}

// Bad: public setter, rule lives in the controller
claim.Status = "Approved";
```

## 6. Async, errors, nullability
- Async all the way, pass `CancellationToken` through every I/O call. Never `.Result`, `.Wait()` or `async void` (except event handlers).
- Use the `Result` pattern (or typed domain errors) for expected failures; exceptions only for the unexpected. Never `catch (Exception)` to ignore; catch specific types, log with context, rethrow or translate.
- No `null` returned for "not found" from commands; use explicit results. Never suppress nullable warnings with `!` without a comment explaining why it is safe.
- Map errors to RFC 7807 problem details in one place (exception/result middleware): validation 400, unauthorised 401, forbidden 403, not found 404, state/concurrency conflict 409. Never expose stack traces or internals.

## 7. API design
- Resource-oriented routes, plural nouns; actions as sub-resources (`POST /claims/{id}/decision`). Correct status codes (201 + `Location`, 204, 409).
- Request/response DTOs are separate from domain entities; request DTOs expose only client-settable fields (no mass assignment of status, assignee, reserve).
- Validate every request with FluentValidation at the edge; domain re-checks invariants.
- List endpoints are paged with a server-side maximum page size. Document everything in OpenAPI with response types and examples.
- Authorise every endpoint with a named policy; default deny. Object-level checks (claimant owns the claim) in the application layer.

## 8. Persistence (EF Core + PostgreSQL)
- Explicit entity configurations (`IEntityTypeConfiguration<T>`), not attributes scattered on the domain.
- Migrations reviewed, small, with a reversible `Down`. Auto-applied at startup in dev only.
- Money: `decimal(18,2)` plus ISO 4217 currency in a `Money` value object; never `double`/`float`.
- Concurrency: `rowVersion` (xmin) token; handle `DbUpdateConcurrencyException` → 409. Claim pick-up must be race-safe and tested.
- No N+1 queries: project to DTOs with `Select`, use `AsNoTracking()` for reads, index columns used in filters (status, market, assignee, createdAt).
- Never build SQL with string concatenation. No cross-service database access.

## 9. Messaging
- Outbox row written in the same transaction as the state change; a publisher background service sends it. Never publish from a request handler directly.
- Envelope: `eventId, type, version, occurredAt, claimId, market, payload`; additive changes only within a version; key = `claimId`.
- Consumers are idempotent on `eventId`, commit offsets after processing, retry with backoff, then DLQ with error context. They project; they do not hold business rules.
- Propagate `correlationId` through HTTP headers, outbox, Kafka headers and logs.

## 10. Logging, config, security
- Serilog structured logging: `_logger.LogInformation("Claim {ClaimId} approved by {OfficerId}", id, officer)`. Never string-interpolate into log templates. Never log secrets, tokens or personal data.
- Configuration via options classes (`IOptions<T>`) validated at startup; secrets from environment variables; no secrets in code, compose files or git. Provide `.env.example`.
- Treat all external input (HTTP, Kafka) as untrusted. Pin dependencies and check `dotnet list package --vulnerable`.

## 11. Time and randomness
- Inject `IClock`/`TimeProvider` and an id generator; never call `DateTime.UtcNow` or `Guid.NewGuid()` in domain logic. Store UTC; format at the edge.

## 12. Comments and documentation
- Code explains *what*; comments explain *why* (a constraint, a trade-off, a non-obvious rule). No commented-out code. No TODO without a reason and an issue/task reference.
- Public API surface has XML docs only where the name is not enough. ADR for each significant decision.

## 13. Tests (see `qa-test-engineer`)
- New behaviour ships with tests in the same change. Names: `Method_Scenario_Expected`. Arrange/Act/Assert, one behaviour per test.
- Unit tests for domain (every state transition, exposure calculation); integration tests with Testcontainers for endpoints, auth, concurrency, outbox atomicity and consumer idempotency.
- Deterministic: injected clock, no sleeps, no shared state, no network except containers.

## 14. Git
- Small conventional commits (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`), imperative subject under 72 chars, body explains why. One concern per commit. Do not commit generated binaries, `.env` or secrets.

## 15. Pre-handoff checklist (every coding agent, every change)
- [ ] `dotnet build` zero warnings; `dotnet format --verify-no-changes` clean
- [ ] `dotnet test` green; new behaviour covered
- [ ] No rule from sections 5–11 violated (domain rules in Domain, async + cancellation, problem details, authorisation, money as `decimal`, outbox used)
- [ ] No dead code, commented-out code, unexplained TODOs, magic values or secrets
- [ ] Diff contains only what the story needs
- [ ] Ready for `code-reviewer` and `security-reviewer`
