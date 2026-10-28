namespace Chubb.Notification.Domain;

public sealed class ProcessedEvent
{
    private ProcessedEvent()
    {
    }

    public Guid EventId { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }

    public static ProcessedEvent Create(Guid eventId, DateTimeOffset processedAt) =>
        new() { EventId = eventId, ProcessedAt = processedAt };
}
