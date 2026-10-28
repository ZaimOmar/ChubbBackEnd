using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

public sealed record StaffClaimView(
    Guid Id,
    string ClaimNumber,
    ClaimType Type,
    string Market,
    string Currency,
    ClaimStatus Status,
    DateOnly IncidentDate,
    string Description,
    decimal EstimatedAmount,
    Guid ClaimantId,
    Guid PolicyId,
    Guid? AssignedOfficerId,
    decimal? AssessedAmount,
    decimal? NetPayable,
    decimal Reserve,
    decimal? Payable,
    OpenProposalView? OpenProposal,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);
