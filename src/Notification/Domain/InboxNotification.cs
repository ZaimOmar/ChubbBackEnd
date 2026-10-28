using Chubb.Contracts;

namespace Chubb.Notification.Domain;

/// <summary>An alert for one claimant (by user id) or for every manager (by role). The message is Public text only.</summary>
public sealed class InboxNotification
{
    private InboxNotification()
    {
    }

    public Guid Id { get; private set; }

    public Guid? RecipientUserId { get; private set; }

    public string? RecipientRole { get; private set; }

    public NotificationKind Kind { get; private set; }

    public Guid ClaimId { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static InboxNotification ForClaimant(Guid claimantId, NotificationKind kind, Guid claimId, string message, DateTimeOffset createdAt) =>
        new() { Id = Guid.NewGuid(), RecipientUserId = claimantId, Kind = kind, ClaimId = claimId, Message = message, CreatedAt = createdAt };

    public static InboxNotification ForManagers(NotificationKind kind, Guid claimId, string message, DateTimeOffset createdAt) =>
        new() { Id = Guid.NewGuid(), RecipientRole = Roles.Manager, Kind = kind, ClaimId = claimId, Message = message, CreatedAt = createdAt };

    public void MarkRead(DateTimeOffset readAt) => ReadAt ??= readAt;
}
