using Chubb.Claims.Application;
using Chubb.Claims.Domain;
using Chubb.Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Infrastructure.Seeding;

internal sealed class ClaimsSeeder(
    ClaimsDbContext db,
    ICommandTransaction transaction,
    SeedClaimBuilder builder,
    IIdGenerator ids,
    TimeProvider clock)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Set<Policy>().AnyAsync(cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        try
        {
            await transaction.BeginAsync(cancellationToken);
            db.Set<Policy>().AddRange(SeedData.Policies(ids.NewId, now));
            await db.SaveChangesAsync(cancellationToken);
            await BuildClaimsAsync(now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private async Task BuildClaimsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var scenarios = SeedData.Scenarios();
        for (var index = 0; index < scenarios.Count; index++)
        {
            var start = now.AddDays(-scenarios[index].DaysAgo);
            await builder.BuildAsync(scenarios[index], index, start, cancellationToken);
        }
    }
}
