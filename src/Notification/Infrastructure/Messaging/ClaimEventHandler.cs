using System.Text.Json;
using Chubb.Contracts.Events;
using Chubb.Notification.Domain;
using Chubb.Notification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Notification.Infrastructure.Messaging;

public sealed class ClaimEventHandler(
    NotificationDbContext db,
    INotificationDelivery delivery,
    TimeProvider clock,
    ILogger<ClaimEventHandler> logger)
{
    public async Task HandleAsync(string message, CancellationToken cancellationToken)
    {
        var envelope = ReadEnvelope(message);
        if (!EventNotificationRules.IsKnown(envelope))
        {
            logger.LogInformation("Event {EventId} of type {EventType} version {EventVersion} raises no notification", envelope.EventId, envelope.Type, envelope.Version);
            return;
        }

        if (await db.ProcessedEvents.AnyAsync(processed => processed.EventId == envelope.EventId, cancellationToken))
        {
            logger.LogInformation("Event {EventId} was already processed", envelope.EventId);
            return;
        }

        var notification = EventNotificationRules.Create(envelope);
        db.Notifications.Add(notification);
        db.ProcessedEvents.Add(ProcessedEvent.Create(envelope.EventId, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        delivery.Deliver(notification);
    }

    private static EventEnvelope ReadEnvelope(string message)
    {
        try
        {
            return JsonSerializer.Deserialize<EventEnvelope>(message, EventJson.Options)
                ?? throw new InvalidEventException("The message is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidEventException("The message is not a valid event envelope.", exception);
        }
    }
}
