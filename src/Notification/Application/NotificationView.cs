namespace Chubb.Notification.Application;

public sealed record NotificationView(Guid Id, string Kind, Guid ClaimId, string Message, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record NotificationPage(IReadOnlyList<NotificationView> Items, int Page, int PageSize, int Total);
