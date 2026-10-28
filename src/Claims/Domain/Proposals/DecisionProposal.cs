namespace Chubb.Claims.Domain;

public sealed record DecisionProposal(
    ProposalOutcome Outcome,
    RejectReasonCode? ReasonCode,
    string Rationale,
    string? Justification,
    string? ClaimantReason = null);
