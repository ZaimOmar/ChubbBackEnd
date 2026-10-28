using Chubb.Notification.Domain;

namespace Chubb.Notification.Infrastructure.Messaging;

/// <summary>Stub for email or SMS: it only logs ids and the kind, never the message or any recipient detail.</summary>
public sealed class LogNotificationDelivery(ILogger<LogNotificationDelivery> logger) : INotificationDelivery
{
    public void Deliver(InboxNotification notification) =>
        logger.LogInformation(
            "Delivery stub: notification {NotificationId} of kind {Kind} for claim {ClaimId}",
            notification.Id, notification.Kind, notification.ClaimId);
}
