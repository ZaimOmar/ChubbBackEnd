using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chubb.Claims.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ClaimHistoryAppendOnlyTests(PostgresFixture postgres)
{
    private const string PermissionDenied = PostgresErrorCodes.InsufficientPrivilege;
    private const string RestrictViolation = PostgresErrorCodes.RestrictViolation;

    [Theory]
    [InlineData("UPDATE claims.claim_history SET summary = 'tampered'")]
    [InlineData("DELETE FROM claims.claim_history")]
    [InlineData("TRUNCATE claims.claim_history")]
    public async Task ClaimHistory_UpdateDeleteTruncateAsOwner_FailWithPermissionDenied(string statement)
    {
        var connectionString = await SeedClaimAsync();

        var failure = await Sql.FailureOfAsync(connectionString, statement);

        failure.SqlState.Should().Be(PermissionDenied);
    }

    [Theory]
    [InlineData("UPDATE claims.claim_history SET summary = 'tampered'")]
    [InlineData("DELETE FROM claims.claim_history")]
    [InlineData("TRUNCATE claims.claim_history")]
    [InlineData("TRUNCATE claims.claims CASCADE")]
    public async Task ClaimHistory_AfterRegrant_TriggersBlockUpdateDeleteAndTruncate(string statement)
    {
        var connectionString = await SeedClaimAsync();
        await Sql.ExecuteAsync(connectionString, "GRANT UPDATE, DELETE, TRUNCATE ON claims.claim_history TO CURRENT_USER");

        var failure = await Sql.FailureOfAsync(connectionString, statement);

        failure.SqlState.Should().Be(RestrictViolation);
    }

    [Fact]
    public async Task ClaimHistory_Insert_RemainsAllowed()
    {
        var connectionString = await SeedClaimAsync();

        var count = await Sql.ScalarAsync<long>(connectionString, "SELECT count(*) FROM claims.claim_history");

        count.Should().Be(1);
    }

    [Fact]
    public async Task ClaimHistory_PublicEntryWithMakerReasonOrReference_RejectedByCheckConstraint()
    {
        var connectionString = await SeedClaimAsync();

        var failure = await Sql.FailureOfAsync(connectionString, InsertSql("'Public'", "maker_id", "gen_random_uuid()"));

        failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task ClaimHistory_LinkToEntryOfOtherClaim_RejectedByCompositeForeignKey()
    {
        var data = new TestData(await postgres.CreateMigratedDatabaseAsync());
        var claimId = await data.SubmitAsync();
        var otherClaimEntryId = await EntryOf(await data.SubmitAsync(), data);

        var failure = await Sql.FailureOfAsync(
            ConnectionOf(data), InsertSql("'Internal'", "referenced_entry_id", $"'{otherClaimEntryId}'", claimId));

        failure.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task ClaimHistory_LinkToEntryOfSameClaim_Accepted()
    {
        var data = new TestData(await postgres.CreateMigratedDatabaseAsync());
        var claimId = await data.SubmitAsync();
        var entryId = await EntryOf(claimId, data);

        await Sql.ExecuteAsync(ConnectionOf(data), InsertSql("'Internal'", "referenced_entry_id", $"'{entryId}'", claimId));

        (await Sql.ScalarAsync<long>(ConnectionOf(data), "SELECT count(*) FROM claims.claim_history")).Should().Be(2);
    }

    private async Task<string> SeedClaimAsync()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        await new TestData(connectionString).SubmitAsync();
        return connectionString;
    }

    private static string ConnectionOf(TestData data)
    {
        using var db = data.NewContext();
        return db.Database.GetConnectionString()!;
    }

    private static async Task<Guid> EntryOf(Guid claimId, TestData data) =>
        await Sql.ScalarAsync<Guid>(ConnectionOf(data), $"SELECT id FROM claims.claim_history WHERE claim_id = '{claimId}'");

    private static string InsertSql(string visibility, string column, string value, Guid? claimId = null) => $"""
        INSERT INTO claims.claim_history
            (id, claim_id, occurred_at, event_type, actor_id, actor_role, summary, visibility, correlation_id, {column})
        VALUES (gen_random_uuid(), {(claimId is null ? "(SELECT id FROM claims.claims LIMIT 1)" : $"'{claimId}'")},
            now(), 'Assessed', gen_random_uuid(), 'Officer', 'x', {visibility}, gen_random_uuid(), {value})
        """;
}
