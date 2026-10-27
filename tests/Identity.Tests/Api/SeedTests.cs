using Chubb.Identity.Domain;
using Chubb.Identity.Infrastructure.Persistence;
using Chubb.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chubb.Identity.Tests.Api;

[Collection(PostgresCollection.Name)]
public sealed class SeedTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Seed_OnStartup_CreatesSixUsersWithExpectedRoles()
    {
        using var keys = new TestKeyPair();
        await using var factory = new IdentityApiFactory(await postgres.CreateDatabaseAsync(), keys);

        var users = await LoadUsers(factory);

        users.Should().HaveCount(6);
        users.Single(user => user.Email == "officer.manager@chubb.test").Roles
            .Should().BeEquivalentTo([Role.Officer, Role.Manager]);
        users.Single(user => user.Email == "claimant@chubb.test").Roles.Should().Equal(Role.Claimant);
        users.Single(user => user.Email == "claimant2@chubb.test").Market.Should().Be("HK");
        users.Select(user => user.PasswordHash).Should().NotContain(IdentityApiFactory.SeedPassword);
    }

    [Fact]
    public async Task Seed_RunTwice_DoesNotDuplicateUsers()
    {
        using var keys = new TestKeyPair();
        await using var factory = new IdentityApiFactory(await postgres.CreateDatabaseAsync(), keys);
        await LoadUsers(factory);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(CancellationToken.None);
        }

        (await LoadUsers(factory)).Should().HaveCount(6);
    }

    private static async Task<List<User>> LoadUsers(IdentityApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.Users.Include(user => user.UserRoles).AsNoTracking().ToListAsync();
    }
}
