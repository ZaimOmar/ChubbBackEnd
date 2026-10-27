using Chubb.Contracts;
using Chubb.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chubb.Identity.Infrastructure.Persistence;

public sealed class IdentitySeeder(
    IdentityDbContext db,
    PasswordVerifier passwordVerifier,
    IOptions<SeedOptions> options,
    TimeProvider timeProvider,
    ILogger<IdentitySeeder> logger)
{
    private static readonly SeedUser[] SeedUsers =
    [
        new(DevSeed.SeedUserIds.Claimant, "claimant@chubb.test", "Alex Claimant", Markets.Singapore, [Role.Claimant]),
        new(DevSeed.SeedUserIds.Officer, "officer@chubb.test", "Olivia Officer", Markets.Singapore, [Role.Officer]),
        new(DevSeed.SeedUserIds.Manager, "manager@chubb.test", "Marcus Manager", Markets.Singapore, [Role.Manager]),
        new(DevSeed.SeedUserIds.OfficerManager, "officer.manager@chubb.test", "Dana Dual", Markets.Singapore, [Role.Officer, Role.Manager]),
        new(DevSeed.SeedUserIds.SecondClaimant, "claimant2@chubb.test", "Bao Claimant", Markets.HongKong, [Role.Claimant]),
        new(DevSeed.SeedUserIds.SecondOfficer, "officer2@chubb.test", "Omar Officer", Markets.Singapore, [Role.Officer]),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existingIds = await db.Users.Select(user => user.Id).ToListAsync(cancellationToken);
        var missing = SeedUsers.Where(seed => !existingIds.Contains(seed.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var passwordHash = passwordVerifier.Hash(options.Value.SeedPassword);
        foreach (var seed in missing)
        {
            db.Users.Add(CreateUser(seed, passwordHash));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} identity users", missing.Count);
    }

    private User CreateUser(SeedUser seed, string passwordHash)
    {
        var result = User.Create(new NewUser(
            seed.Id, seed.Email, seed.DisplayName, seed.Market, passwordHash, seed.Roles, timeProvider.GetUtcNow()));
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Seed user {seed.Id} is invalid: {result.Error.Code}");
    }

    private sealed record SeedUser(Guid Id, string Email, string DisplayName, string Market, Role[] Roles);
}
