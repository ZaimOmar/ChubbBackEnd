using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public enum ClaimListSort
{
    Newest,
    Oldest,
}

/// <summary>Per-column table filters. Amounts are the estimated amount in the claim's own currency; dates are inclusive UTC days of submission.</summary>
public sealed record ClaimColumnCriteria(
    string? ClaimNumber, decimal? MinAmount, decimal? MaxAmount, DateOnly? SubmittedFrom, DateOnly? SubmittedTo);

public static class OutstandingClaims
{
    /// <summary>The statuses the Reporting outstanding dashboard counts: everything except Rejected and Settled (BR-77).</summary>
    public static readonly IReadOnlyList<ClaimStatus> Statuses = Enum.GetValues<ClaimStatus>()
        .Where(status => status is not (ClaimStatus.Rejected or ClaimStatus.Settled))
        .ToList();
}

public sealed record ClaimListFilter(
    string? Market, ClaimType? Type, ClaimStatus? Status, Guid? AssigneeId, ClaimColumnCriteria Columns, bool OutstandingOnly, int Page, int PageSize, ClaimListSort Sort);

/// <summary>Status is the claimant-facing status, so UnderReview also matches claims awaiting a decision approval.</summary>
public sealed record MyClaimsFilter(
    Guid ClaimantId, string? Market, ClaimType? Type, ClaimStatus? Status, ClaimColumnCriteria Columns, int Page, int PageSize, ClaimListSort Sort);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

public interface IClaimQueries
{
    /// <summary>The claim, or null when it does not exist or a claimant does not own it (one answer for both).</summary>
    Task<Claim?> FindAsync(Guid claimId, Actor actor, CancellationToken cancellationToken);

    Task<PagedResult<Claim>> ListAsync(ClaimListFilter filter, CancellationToken cancellationToken);

    Task<PagedResult<Claim>> ListMineAsync(MyClaimsFilter filter, CancellationToken cancellationToken);

    /// <summary>The whole trail in (occurredAt, id) order, or null when the claim is not visible to the actor.</summary>
    Task<IReadOnlyList<TrailEntry>?> GetTrailAsync(Guid claimId, Actor actor, CancellationToken cancellationToken);
}
