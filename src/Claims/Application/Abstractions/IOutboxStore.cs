namespace Chubb.Claims.Application;

/// <summary>The only seam the outbox publisher uses; it brackets fetch, publish and mark in its own short transaction.</summary>
public interface IOutboxStore
{
    /// <summary>Unpublished rows by <c>sequence</c>, locked with <c>FOR UPDATE SKIP LOCKED</c> until the transaction ends.</summary>
    Task<IReadOnlyList<OutboxRecord>> FetchUnpublishedBatchAsync(int max, CancellationToken cancellationToken);

    Task MarkPublishedAsync(Guid eventId, DateTimeOffset publishedAt, CancellationToken cancellationToken);

    /// <summary>Adds one attempt and stores the already sanitised error, cut to the column limit.</summary>
    Task RecordFailureAsync(Guid eventId, string sanitisedError, CancellationToken cancellationToken);
}
