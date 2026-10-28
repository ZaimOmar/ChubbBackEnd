namespace Chubb.Claims.Infrastructure.Persistence;

internal sealed class ClaimNumberSequence
{
    public required string Market { get; init; }

    public required int Year { get; init; }

    public required int LastValue { get; init; }
}
