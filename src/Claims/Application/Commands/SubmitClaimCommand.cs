using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public sealed record SubmitClaimCommand(
    string PolicyNumber,
    string Market,
    DateOnly IncidentDate,
    string Description,
    decimal EstimatedAmount,
    ClaimDetails Details);
