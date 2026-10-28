using Chubb.Claims.Infrastructure.Persistence;

namespace Chubb.Claims.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ClaimNumberSequenceTests(PostgresFixture postgres)
{
    private const int ConcurrentSubmits = 20;

    [Fact]
    public async Task NextClaimSequence_TwentyConcurrent_NumbersUniqueAndContiguous()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        var numbers = await Task.WhenAll(Enumerable.Range(0, ConcurrentSubmits).Select(_ => NextInOwnTransaction(connectionString)));

        numbers.Order().Should().Equal(Enumerable.Range(1, ConcurrentSubmits));
    }

    [Fact]
    public async Task NextClaimSequence_RolledBack_DoesNotConsumeValue()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using (var db = PostgresFixture.NewContext(connectionString))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await new EfClaimRepository(db).NextClaimSequenceAsync("SG", 2026, default);
            await transaction.RollbackAsync();
        }

        (await NextInOwnTransaction(connectionString)).Should().Be(1);
    }

    [Fact]
    public async Task NextClaimSequence_OtherMarketOrYear_StartsAtOne()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var db = PostgresFixture.NewContext(connectionString);
        var repository = new EfClaimRepository(db);

        var sequence = new[]
        {
            await repository.NextClaimSequenceAsync("SG", 2026, default),
            await repository.NextClaimSequenceAsync("HK", 2026, default),
            await repository.NextClaimSequenceAsync("SG", 2027, default),
        };

        sequence.Should().Equal(1, 1, 1);
    }

    private static async Task<int> NextInOwnTransaction(string connectionString)
    {
        await using var db = PostgresFixture.NewContext(connectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var number = await new EfClaimRepository(db).NextClaimSequenceAsync("SG", 2026, default);
        await transaction.CommitAsync();
        return number;
    }
}
