using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

public sealed record SubmitClaimRequest
{
    public required string PolicyNumber { get; init; }

    public required ClaimType Type { get; init; }

    public required string Market { get; init; }

    public required DateOnly IncidentDate { get; init; }

    public required string Description { get; init; }

    public required decimal EstimatedAmount { get; init; }

    public MotorDetailsRequest? MotorDetails { get; init; }

    public PropertyDetailsRequest? PropertyDetails { get; init; }
}
