using Chubb.Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Chubb.Claims.Persistence.Tests;

// One container for all tests. Each test gets its own database owned by a non-superuser login role,
// so REVOKE and current_user behave as they do in compose.
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string OwnerRole = "claims_test_owner";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly string _ownerPassword = Guid.NewGuid().ToString("N");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await Sql.ExecuteAsync(_container.GetConnectionString(), $"CREATE ROLE {OwnerRole} LOGIN NOSUPERUSER PASSWORD '{_ownerPassword}'");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"claims_{Guid.NewGuid():N}";
        await Sql.ExecuteAsync(_container.GetConnectionString(), $"CREATE DATABASE {name} OWNER {OwnerRole}");
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
            Username = OwnerRole,
            Password = _ownerPassword,
            Pooling = false,
        }.ConnectionString;
    }

    public async Task<string> CreateMigratedDatabaseAsync()
    {
        var connectionString = await CreateEmptyDatabaseAsync();
        await using var db = NewContext(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    public static ClaimsDbContext NewContext(string connectionString) =>
        new(ClaimsDbContextOptions.Build(connectionString));
}
