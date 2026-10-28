using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

public sealed record ProposeDecisionRequest
{
    public required ProposalOutcome Outcome { get; init; }

    public string? ReasonCode { get; init; }

    public required string Rationale { get; init; }

    public string? Justification { get; init; }

    public string? ClaimantReason { get; init; }
}
