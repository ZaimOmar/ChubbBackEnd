using Microsoft.Extensions.Hosting;

namespace Chubb.Claims.Infrastructure.Seeding;

// Registered in Development only. It runs at host start, after the migration in Program.
internal sealed class ClaimsSeedingService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ClaimsSeedingService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.IsClaimsDatabaseConfigured())
        {
            logger.LogWarning("Claims database is not configured; seeding was skipped");
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ClaimsSeeder>().SeedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
