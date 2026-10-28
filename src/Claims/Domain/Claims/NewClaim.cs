namespace Chubb.Claims.Domain;

public sealed record NewClaim(
    ClaimNumber Number,
    string Market,
    DateOnly IncidentDate,
    string Description,
    Money Estimated,
    ClaimDetails Details);
