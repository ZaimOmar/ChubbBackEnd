using Chubb.Claims.Application;
using Chubb.Claims.Domain;
using Chubb.Claims.Infrastructure.Idempotency;
using Chubb.Claims.Infrastructure.Identifiers;
using Chubb.Claims.Infrastructure.Outbox;
using Chubb.Claims.Infrastructure.Persistence;
using Chubb.Claims.Infrastructure.Queries;
using Chubb.Claims.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Infrastructure;

public static class PersistenceSetup
{
    public const string ConnectionName = "ClaimsDb";

    public static IServiceCollection AddClaimsPersistence(this IServiceCollection services)
    {
        services.AddDbContext<ClaimsDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionName}' is not configured.");
            ClaimsDbContextOptions.Configure(options, connectionString);
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();
        services.AddScoped<IClaimRepository, EfClaimRepository>();
        services.AddScoped<IClaimUnitOfWork, EfClaimUnitOfWork>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IOutboxStore, PostgresOutboxStore>();
        services.AddScoped<IClaimQueries, ClaimQueries>();
        services.AddScoped<ICommandTransaction, EfCommandTransaction>();
        services.AddScoped<IIdempotencyStore, PostgresIdempotencyStore>();
        services.AddScoped<IRefusedAttemptRecorder, RefusedAttemptRecorder>();
        return services;
    }

    public static IServiceCollection AddClaimsSeeding(this IServiceCollection services)
    {
        services.AddScoped<SeedClaimBuilder>();
        services.AddScoped<ClaimsSeeder>();
        services.AddHostedService<ClaimsSeedingService>();
        return services;
    }

    internal static bool IsClaimsDatabaseConfigured(this IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionName));

    // A host without a connection string (the host tests) starts without a database; any database use then fails loudly.
    public static async Task MigrateClaimsDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.IsClaimsDatabaseConfigured())
        {
            app.Logger.LogWarning("Connection string {ConnectionName} is not configured; migrations were skipped", ConnectionName);
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        await db.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
    }
}
