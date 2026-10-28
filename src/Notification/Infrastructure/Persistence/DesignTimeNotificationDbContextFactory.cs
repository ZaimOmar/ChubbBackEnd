using Microsoft.EntityFrameworkCore.Design;

namespace Chubb.Notification.Infrastructure.Persistence;

// Used only by `dotnet ef`; it never connects, so the connection string holds no credentials.
internal sealed class DesignTimeNotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args) =>
        new(NotificationDbContextOptions.Build("Host=localhost;Database=notification_design"));
}
