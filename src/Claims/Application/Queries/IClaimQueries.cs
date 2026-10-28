using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public enum ClaimListSort
{
    Newest,
    Oldest,
}

public sealed record ClaimListFilter(
    string? Market, ClaimType? Type, ClaimStatus? Status, Guid? AssigneeId, int Page, int PageSize, ClaimListSort Sort);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

public interface IClaimQueries
{
    /// <summary>The claim, or null when it does not exist or a claimant does not own it (one answer for both).</summary>
    Task<Claim?> FindAsync(Guid claimId, Actor actor, CancellationToken cancellationToken);

    Task<PagedResult<Claim>> ListAsync(ClaimListFilter filter, CancellationToken cancellationToken);

    /// <summary>The whole trail in (occurredAt, id) order, or null when the claim is not visible to the actor.</summary>
    Task<IReadOnlyList<TrailEntry>?> GetTrailAsync(Guid claimId, Actor actor, CancellationToken cancellationToken);
}
