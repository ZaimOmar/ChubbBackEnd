using Microsoft.EntityFrameworkCore;

namespace Chubb.Notification.Infrastructure.Persistence;

public static class NotificationDbContextOptions
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static DbContextOptions<NotificationDbContext> Build(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<NotificationDbContext>();
        Configure(builder, connectionString);
        return builder.Options;
    }

    public static void Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, NotificationDbContext.Schema));
}
