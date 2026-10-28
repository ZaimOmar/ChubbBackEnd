using System.ComponentModel.DataAnnotations;

namespace Chubb.Notification.Infrastructure.Messaging;

public sealed class KafkaConsumerOptions
{
    public const string SectionName = "Kafka";
    public const string BootstrapServersKey = "Kafka:BootstrapServers";

    [Required]
    public string BootstrapServers { get; init; } = string.Empty;

    [Required]
    public string GroupId { get; init; } = "notification";

    [Range(1, 10)]
    public int MaxAttempts { get; init; } = 3;

    public TimeSpan RetryBackoff { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan PollTimeout { get; init; } = TimeSpan.FromSeconds(1);
}
