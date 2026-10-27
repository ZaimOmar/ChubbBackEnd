using Microsoft.EntityFrameworkCore;

namespace Chubb.Identity.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IdentityDbContext db, IdentitySeeder seeder)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await seeder.SeedAsync(cancellationToken);
    }
}
