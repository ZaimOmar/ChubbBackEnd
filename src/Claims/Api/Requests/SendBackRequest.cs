namespace Chubb.Claims.Api;

public sealed record SendBackRequest
{
    public required string Reason { get; init; }
}
