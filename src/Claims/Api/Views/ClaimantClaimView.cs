using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

// A different type from the staff view on purpose: masking can never be a forgotten null.
public sealed record ClaimantClaimView(
    Guid Id,
    string ClaimNumber,
    ClaimType Type,
    string Market,
    string Currency,
    ClaimStatus Status,
    DateOnly IncidentDate,
    string Description,
    decimal EstimatedAmount,
    decimal? ApprovedAmount,
    RejectionReasonView? RejectionReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);
