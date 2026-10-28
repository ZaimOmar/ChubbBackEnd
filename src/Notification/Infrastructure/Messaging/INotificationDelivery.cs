using Chubb.Notification.Domain;

namespace Chubb.Notification.Infrastructure.Messaging;

public interface INotificationDelivery
{
    void Deliver(InboxNotification notification);
}
