using Chubb.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Chubb.Identity.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_ApplyAndRevert_Succeed()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(await postgres.CreateDatabaseAsync()).Options;
        await using var db = new IdentityDbContext(options);

        await db.Database.MigrateAsync();
        (await TableExists(db, "users")).Should().BeTrue();
        (await TableExists(db, "user_roles")).Should().BeTrue();

        await db.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);
        (await TableExists(db, "users")).Should().BeFalse();
        (await TableExists(db, "user_roles")).Should().BeFalse();
    }

    private static async Task<bool> TableExists(IdentityDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"select to_regclass('public.{table}') is not null";
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
