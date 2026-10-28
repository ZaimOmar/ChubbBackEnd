using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Infrastructure.Persistence;

public static class ClaimsDbContextOptions
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static DbContextOptions<ClaimsDbContext> Build(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<ClaimsDbContext>();
        Configure(builder, connectionString);
        return builder.Options;
    }

    // No EnableRetryOnFailure: the idempotency executor owns the transaction and a retrying strategy would break it (ADR-009).
    public static void Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, ClaimsDbContext.Schema));
}
