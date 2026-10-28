namespace Chubb.Claims.Infrastructure.Outbox;

internal sealed class OutboxMessage
{
    public required Guid EventId { get; init; }

    public long Sequence { get; private set; }

    public required Guid ClaimId { get; init; }

    public required string Type { get; init; }

    public required int Version { get; init; }

    public required string Envelope { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }
}
