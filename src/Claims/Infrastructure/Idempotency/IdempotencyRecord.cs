namespace Chubb.Claims.Infrastructure.Idempotency;

internal sealed class IdempotencyRecord
{
    public required Guid ActorId { get; init; }

    public required string Route { get; init; }

    public required string Key { get; init; }

    public required byte[] RequestHash { get; init; }

    public required int StatusCode { get; init; }

    public required byte[] ResponseBody { get; init; }

    public required string ContentType { get; init; }

    public string? Location { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}
