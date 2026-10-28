using Chubb.Notification.Domain;
using Chubb.Notification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Notification.Application;

public sealed class InboxService(NotificationDbContext db, TimeProvider clock)
{
    public const int MaxPageSize = 100;

    public async Task<NotificationPage> ListAsync(InboxScope scope, int page, int pageSize, CancellationToken cancellationToken)
    {
        var visible = Visible(scope);
        var total = await visible.CountAsync(cancellationToken);
        var items = await visible
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(notification => new NotificationView(
                notification.Id, notification.Kind.ToString(), notification.ClaimId, notification.Message, notification.CreatedAt, notification.ReadAt))
            .ToListAsync(cancellationToken);
        return new NotificationPage(items, page, pageSize, total);
    }

    /// <summary>Returns false when the notification does not exist or is not in the caller's scope (the same answer for both).</summary>
    public async Task<bool> MarkReadAsync(InboxScope scope, Guid id, CancellationToken cancellationToken)
    {
        var notification = await Visible(scope).SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        notification.MarkRead(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<InboxNotification> Visible(InboxScope scope) =>
        scope.UserId is { } userId
            ? db.Notifications.Where(notification => notification.RecipientUserId == userId)
            : db.Notifications.Where(notification => notification.RecipientRole == scope.Role);
}
