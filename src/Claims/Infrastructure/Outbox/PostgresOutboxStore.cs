using Chubb.Claims.Application;
using Chubb.Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Infrastructure.Outbox;

public sealed class PostgresOutboxStore(ClaimsDbContext db) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxRecord>> FetchUnpublishedBatchAsync(int max, CancellationToken cancellationToken)
    {
        var messages = await db.Set<OutboxMessage>().FromSql($"""
            SELECT * FROM claims.outbox
            WHERE published_at IS NULL
            ORDER BY sequence
            LIMIT {max}
            FOR UPDATE SKIP LOCKED
            """).AsNoTracking().ToListAsync(cancellationToken);
        return messages
            .Select(message => new OutboxRecord(
                message.EventId, message.Sequence, message.ClaimId, message.Type, message.Version, message.Envelope, message.Attempts))
            .ToList();
    }

    public async Task MarkPublishedAsync(Guid eventId, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        await db.Set<OutboxMessage>()
            .Where(message => message.EventId == eventId)
            .ExecuteUpdateAsync(update => update.SetProperty(message => message.PublishedAt, publishedAt), cancellationToken);

    public async Task RecordFailureAsync(Guid eventId, string sanitisedError, CancellationToken cancellationToken)
    {
        var storedError = sanitisedError.Length > OutboxMessageConfiguration.LastErrorMaxLength
            ? sanitisedError[..OutboxMessageConfiguration.LastErrorMaxLength]
            : sanitisedError;
        await db.Set<OutboxMessage>()
            .Where(message => message.EventId == eventId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(message => message.Attempts, message => message.Attempts + 1)
                    .SetProperty(message => message.LastError, storedError),
                cancellationToken);
    }
}
