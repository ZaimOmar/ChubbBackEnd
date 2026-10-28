using Chubb.Claims.Application;
using Chubb.Claims.Domain;
using Chubb.Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Infrastructure.Queries;

public sealed class ClaimQueries(ClaimsDbContext db) : IClaimQueries
{
    public Task<Claim?> FindAsync(Guid claimId, Actor actor, CancellationToken cancellationToken) =>
        VisibleTo(actor).Where(claim => claim.Id == claimId).SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<Claim>> ListAsync(ClaimListFilter filter, CancellationToken cancellationToken)
    {
        var matching = Filtered(filter);
        var total = await matching.CountAsync(cancellationToken);
        var items = await Ordered(matching, filter.Sort)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Claim>(items, total);
    }

    public async Task<IReadOnlyList<TrailEntry>?> GetTrailAsync(Guid claimId, Actor actor, CancellationToken cancellationToken)
    {
        var isVisible = await VisibleTo(actor).AnyAsync(claim => claim.Id == claimId, cancellationToken);
        if (!isVisible)
        {
            return null;
        }

        // Ordered in SQL: .NET Guid order is not PostgreSQL uuid order, so ties are never sorted in memory.
        return await db.Set<TrailEntry>().AsNoTracking()
            .Where(entry => entry.ClaimId == claimId)
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
    }

    // Oldest: the open proposal's age (else the submission time), then claim number, so the checker queue is stable.
    private static IOrderedQueryable<Claim> Ordered(IQueryable<Claim> claims, ClaimListSort sort) => sort == ClaimListSort.Oldest
        ? claims
            .OrderBy(claim => claim.Proposals.Where(proposal => proposal.State == ProposalState.Open)
                .Min(proposal => (DateTimeOffset?)proposal.ProposedAt) ?? claim.SubmittedAt)
            .ThenBy(claim => claim.Number)
        : claims.OrderByDescending(claim => claim.SubmittedAt).ThenBy(claim => claim.Id);

    private IQueryable<Claim> Claims() =>
        db.Set<Claim>().AsNoTracking().Include(claim => claim.Policy).Include(claim => claim.Proposals);

    private IQueryable<Claim> VisibleTo(Actor actor)
    {
        var claims = Claims();
        return actor.IsStaff ? claims : claims.Where(claim => claim.ClaimantId == actor.UserId);
    }

    private IQueryable<Claim> Filtered(ClaimListFilter filter)
    {
        var claims = Claims();
        if (filter.Market is { } market)
        {
            claims = claims.Where(claim => claim.Market == market);
        }

        if (filter.Type is { } type)
        {
            claims = claims.Where(claim => claim.Type == type);
        }

        if (filter.Status is { } status)
        {
            claims = claims.Where(claim => claim.Status == status);
        }

        return filter.AssigneeId is { } assignee ? claims.Where(claim => claim.AssignedOfficerId == assignee) : claims;
    }
}
