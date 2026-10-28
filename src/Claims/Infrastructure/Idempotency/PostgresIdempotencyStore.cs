using Chubb.Claims.Application;
using Chubb.Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Chubb.Claims.Infrastructure.Idempotency;

public sealed class PostgresIdempotencyStore(
    ClaimsDbContext db, IOptions<IdempotencyOptions> options, TimeProvider clock) : IIdempotencyStore
{
    public async Task<bool> TryLockAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        var lockKey = $"{request.ActorId}|{request.Route}|{request.Key}";
        var results = await db.Database
            .SqlQuery<bool>($"""SELECT pg_try_advisory_xact_lock(hashtextextended({lockKey}, 0)) AS "Value" """)
            .ToListAsync(cancellationToken);
        return results.Single();
    }

    public async Task<StoredIdempotencyRecord?> FindAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        var record = await Matching(request).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (record.ExpiresAt <= clock.GetUtcNow())
        {
            await Matching(request).ExecuteDeleteAsync(cancellationToken);
            return null;
        }

        return new StoredIdempotencyRecord(
            record.RequestHash, new StoredResponse(record.StatusCode, record.ResponseBody, record.ContentType, record.Location));
    }

    public async Task AddAsync(IdempotencyRequest request, StoredResponse response, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        db.Set<IdempotencyRecord>().Add(new IdempotencyRecord
        {
            ActorId = request.ActorId,
            Route = request.Route,
            Key = request.Key,
            RequestHash = request.RequestHash,
            StatusCode = response.StatusCode,
            ResponseBody = response.Body,
            ContentType = response.ContentType,
            Location = response.Location,
            CreatedAt = now,
            ExpiresAt = now + options.Value.Retention,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<IdempotencyRecord> Matching(IdempotencyRequest request) =>
        db.Set<IdempotencyRecord>().Where(record =>
            record.ActorId == request.ActorId && record.Route == request.Route && record.Key == request.Key);
}
