using System.Text.Json.Nodes;
using Chubb.Claims.Domain;
using Chubb.Claims.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chubb.Claims.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class OutboxTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OutboxRow_Written_HasEnvelopeFieldsCorrelationCausationAndUnpublishedState()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var data = new TestData(connectionString);
        var claimId = await data.SubmitAsync();

        var entryId = await Sql.ScalarAsync<Guid>(connectionString, "SELECT id FROM claims.claim_history");
        var correlationId = await Sql.ScalarAsync<Guid>(connectionString, "SELECT correlation_id FROM claims.claim_history");
        var envelope = JsonNode.Parse(await Sql.ScalarAsync<string>(connectionString, "SELECT envelope::text FROM claims.outbox"))!;
        envelope["type"]!.GetValue<string>().Should().Be("ClaimSubmitted");
        envelope["version"]!.GetValue<int>().Should().Be(1);
        envelope["claimId"]!.GetValue<Guid>().Should().Be(claimId);
        envelope["market"]!.GetValue<string>().Should().Be("SG");
        envelope["correlationId"]!.GetValue<Guid>().Should().Be(correlationId);
        envelope["causationId"]!.GetValue<Guid>().Should().Be(entryId);
        envelope["payload"]!["status"]!.GetValue<string>().Should().Be("Submitted");
        (await Sql.ScalarAsync<long>(connectionString, "SELECT count(*) FROM claims.outbox WHERE published_at IS NULL AND attempts = 0"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Schema_OutboxLastErrorOver1000Characters_RejectedByCheckConstraint()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        var failure = await Sql.FailureOfAsync(
            connectionString,
            "INSERT INTO claims.outbox (event_id, claim_id, type, version, envelope, occurred_at, attempts, last_error) " +
            "VALUES (gen_random_uuid(), gen_random_uuid(), 'T', 1, '{}', now(), 0, repeat('x', 1001))");

        failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task FetchUnpublishedBatch_ReturnsSequenceOrderAndSkipsPublished()
    {
        var (data, store) = await SeedThreeEventsAsync();
        await using var db = data.NewContext();
        var all = await new PostgresOutboxStore(db).FetchUnpublishedBatchAsync(10, default);
        await new PostgresOutboxStore(db).MarkPublishedAsync(all[0].EventId, DateTimeOffset.UtcNow, default);

        var remaining = await store.FetchUnpublishedBatchAsync(10, default);

        all.Select(record => record.Type).Should().Equal("ClaimSubmitted", "ClaimAssigned", "ClaimAssessed");
        all.Select(record => record.Sequence).Should().BeInAscendingOrder();
        remaining.Select(record => record.EventId).Should().Equal(all.Skip(1).Select(record => record.EventId));
    }

    [Fact]
    public async Task FetchUnpublishedBatch_ConcurrentCallers_GetDisjointRowsViaSkipLocked()
    {
        var (data, _) = await SeedThreeEventsAsync();
        await using var first = data.NewContext();
        await using var second = data.NewContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();

        var firstBatch = await new PostgresOutboxStore(first).FetchUnpublishedBatchAsync(2, default);
        var secondBatch = await new PostgresOutboxStore(second).FetchUnpublishedBatchAsync(2, default);

        firstBatch.Should().HaveCount(2);
        secondBatch.Should().HaveCount(1);
        firstBatch.Select(record => record.EventId).Should().NotIntersectWith(secondBatch.Select(record => record.EventId));
    }

    [Fact]
    public async Task MarkPublished_SetsPublishedAtAndRowLeavesTheBatch()
    {
        var (data, store) = await SeedThreeEventsAsync();
        var first = (await store.FetchUnpublishedBatchAsync(1, default)).Single();
        var publishedAt = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        await store.MarkPublishedAsync(first.EventId, publishedAt, default);

        var batch = await store.FetchUnpublishedBatchAsync(10, default);
        batch.Should().HaveCount(2).And.NotContain(record => record.EventId == first.EventId);
        await using var db = data.NewContext();
        (await Sql.ScalarAsync<DateTime>(db.Database.GetConnectionString()!, $"SELECT published_at FROM claims.outbox WHERE event_id = '{first.EventId}'"))
            .Should().Be(publishedAt.UtcDateTime);
    }

    [Fact]
    public async Task RecordFailure_IncrementsAttemptsAndStoresSanitisedErrorWithinLimit()
    {
        var (data, store) = await SeedThreeEventsAsync();
        var first = (await store.FetchUnpublishedBatchAsync(1, default)).Single();

        await store.RecordFailureAsync(first.EventId, "KafkaException: broker down", default);
        await store.RecordFailureAsync(first.EventId, new string('x', 5000), default);

        var after = (await store.FetchUnpublishedBatchAsync(1, default)).Single();
        after.Attempts.Should().Be(2);
        await using var db = data.NewContext();
        (await Sql.ScalarAsync<int>(db.Database.GetConnectionString()!, $"SELECT length(last_error) FROM claims.outbox WHERE event_id = '{first.EventId}'"))
            .Should().Be(1000);
    }

    private async Task<(TestData Data, PostgresOutboxStore Store)> SeedThreeEventsAsync()
    {
        var data = new TestData(await postgres.CreateMigratedDatabaseAsync());
        var claimId = await data.SubmitAsync();
        await data.RunAsync(claimId, ClaimCommand.PickUp, data.Officer, (claim, context) => claim.PickUp(context).Refusal);
        await data.RunAsync(claimId, ClaimCommand.Assess, data.Officer, (claim, context) =>
            claim.Assess(context, Money.Create(8_000m, claim.Currency), "Inspected").Refusal);
        return (data, new PostgresOutboxStore(data.NewContext()));
    }
}
