namespace Chubb.Claims.Application;

public sealed record StoredIdempotencyRecord(byte[] RequestHash, StoredResponse Response);

/// <summary>All members run inside the executor's transaction.</summary>
public interface IIdempotencyStore
{
    /// <summary>Takes a transaction-scoped advisory lock without waiting; false means the same request is in flight.</summary>
    Task<bool> TryLockAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    /// <summary>The unexpired record, or null. An expired record is deleted in the same transaction.</summary>
    Task<StoredIdempotencyRecord?> FindAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    Task AddAsync(IdempotencyRequest request, StoredResponse response, CancellationToken cancellationToken);
}
