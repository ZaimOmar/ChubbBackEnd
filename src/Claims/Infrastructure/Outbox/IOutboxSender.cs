using Chubb.Claims.Application;

namespace Chubb.Claims.Infrastructure.Outbox;

/// <summary>Sends one outbox row and returns only after the broker acknowledged it. Keeps the publisher free of Kafka types.</summary>
public interface IOutboxSender
{
    /// <exception cref="OutboxSendException">The broker did not acknowledge the row.</exception>
    Task SendAsync(OutboxRecord record, CancellationToken cancellationToken);
}
