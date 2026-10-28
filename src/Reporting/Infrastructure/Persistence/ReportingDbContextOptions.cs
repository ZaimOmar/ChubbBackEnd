using Microsoft.EntityFrameworkCore;

namespace Chubb.Reporting.Infrastructure.Persistence;

public static class ReportingDbContextOptions
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static DbContextOptions<ReportingDbContext> Build(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<ReportingDbContext>();
        Configure(builder, connectionString);
        return builder.Options;
    }

    public static void Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, ReportingDbContext.Schema));
}
