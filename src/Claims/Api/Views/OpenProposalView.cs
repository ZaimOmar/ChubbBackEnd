using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

public sealed record OpenProposalView(
    Guid Id,
    ProposalOutcome Outcome,
    decimal? Amount,
    string? Currency,
    string? ReasonCode,
    string Rationale,
    string? Justification,
    string? ClaimantReason,
    Guid MakerId,
    DateTimeOffset ProposedAt);
