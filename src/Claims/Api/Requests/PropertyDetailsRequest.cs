namespace Chubb.Claims.Api;

public sealed record PropertyDetailsRequest
{
    public required string PropertyAddress { get; init; }

    public required string DamageType { get; init; }
}
