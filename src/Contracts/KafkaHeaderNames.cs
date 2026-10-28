namespace Chubb.Contracts;

/// <summary>Kafka message headers; separate from the HTTP <see cref="HeaderNames"/> so the two kinds cannot be confused.</summary>
public static class KafkaHeaderNames
{
    public const string EventId = "event-id";
    public const string EventType = "event-type";
    public const string EventVersion = "event-version";
}
