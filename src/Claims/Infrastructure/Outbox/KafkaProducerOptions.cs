using System.ComponentModel.DataAnnotations;

namespace Chubb.Claims.Infrastructure.Outbox;

public sealed class KafkaProducerOptions
{
    public const string SectionName = "Kafka";

    [Required]
    public string BootstrapServers { get; init; } = string.Empty;

    public TimeSpan MessageTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
