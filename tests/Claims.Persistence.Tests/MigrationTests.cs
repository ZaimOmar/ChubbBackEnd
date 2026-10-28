using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Chubb.Claims.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture postgres)
{
    private static readonly string[] AllTables =
    [
        "policies",
        "claims",
        "claim_motor_details",
        "claim_property_details",
        "claim_number_sequences",
        "proposals",
        "claim_history",
        "outbox",
        "idempotency_keys",
    ];

    [Fact]
    public async Task Migrations_UpDownToZeroUp_Succeeds()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var db = PostgresFixture.NewContext(connectionString);

        await db.Database.MigrateAsync();
        await AssertTablesExist(connectionString, true);

        await db.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);
        await AssertTablesExist(connectionString, false);

        await db.Database.MigrateAsync();
        await AssertTablesExist(connectionString, true);
    }

    [Fact]
    public async Task Model_AllTablesAndColumns_AreSnakeCase()
    {
        await using var db = PostgresFixture.NewContext(await postgres.CreateEmptyDatabaseAsync());

        var names = db.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => property.GetColumnName())
                .Append(entity.GetTableName()!))
            .Distinct();

        names.Should().OnlyContain(name => System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z][a-z0-9_]*$"));
    }

    [Fact]
    public async Task Migration_ClaimsTable_HasXminSystemColumnOnly()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        var userColumns = await Sql.ScalarAsync<long>(
            connectionString,
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'claims' AND table_name = 'claims' AND column_name = 'xmin'");

        userColumns.Should().Be(0);
    }

    private static async Task AssertTablesExist(string connectionString, bool expected)
    {
        foreach (var table in AllTables)
        {
            var exists = await Sql.ScalarAsync<bool>(connectionString, $"SELECT to_regclass('claims.{table}') IS NOT NULL");
            exists.Should().Be(expected, table);
        }
    }
}
