namespace Chubb.Claims.Infrastructure.Outbox;

/// <summary>What the health check reads: the row the publisher is stuck on, if any. Written by the publisher thread only.</summary>
public sealed class OutboxPublisherState
{
    private volatile StuckRow? _stuck;

    public StuckRow? Stuck => _stuck;

    /// <summary>Returns true when this is a new stuck row, so the caller logs <c>OutboxStuck</c> once per state change.</summary>
    public bool MarkStuck(StuckRow row)
    {
        var isNew = _stuck?.EventId != row.EventId;
        _stuck = row;
        return isNew;
    }

    /// <summary>Returns true when the publisher was stuck and has now recovered.</summary>
    public bool Recover() => Interlocked.Exchange(ref _stuck, null) is not null;
}

public sealed record StuckRow(Guid EventId, long Sequence, int Attempts);
