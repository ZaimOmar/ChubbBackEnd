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
        var matching = ByColumns(Filtered(filter), filter.Columns);
        var total = await matching.CountAsync(cancellationToken);
        var items = await Ordered(matching, filter.Sort)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Claim>(items, total);
    }

    public async Task<PagedResult<Claim>> ListMineAsync(MyClaimsFilter filter, CancellationToken cancellationToken)
    {
        var matching = Claims(filter.Columns.ClaimNumber).Where(claim => claim.ClaimantId == filter.ClaimantId);
        matching = ByColumns(matching, filter.Columns);
        if (filter.Market is { } market)
        {
            matching = matching.Where(claim => claim.Market == market);
        }

        if (filter.Type is { } type)
        {
            matching = matching.Where(claim => claim.Type == type);
        }

        if (filter.Status is { } shown)
        {
            var stored = Enum.GetValues<ClaimStatus>().Where(status => ClaimantView.StatusFor(status) == shown).ToList();
            matching = matching.Where(claim => stored.Contains(claim.Status));
        }

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

    // The claim number has a value converter, which EF cannot compose with ILIKE, so the match is a SQL source.
    // xmin is a system column and is not part of c.*, but the row version maps to it.
    private IQueryable<Claim> Claims(string? numberContains = null)
    {
        var source = numberContains is null
            ? db.Set<Claim>()
            : db.Set<Claim>().FromSql($"SELECT c.*, c.xmin FROM claims.claims c WHERE c.claim_number ILIKE {LikeContains(numberContains)}");
        return source.AsNoTracking().Include(claim => claim.Policy).Include(claim => claim.Proposals);
    }

    private static string LikeContains(string text) =>
        "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private static IQueryable<Claim> ByColumns(IQueryable<Claim> claims, ClaimColumnCriteria columns)
    {
        if (columns.MinAmount is { } min)
        {
            claims = claims.Where(claim => EF.Property<decimal>(claim, "_estimated") >= min);
        }

        if (columns.MaxAmount is { } max)
        {
            claims = claims.Where(claim => EF.Property<decimal>(claim, "_estimated") <= max);
        }

        if (columns.SubmittedFrom is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            claims = claims.Where(claim => claim.SubmittedAt >= start);
        }

        if (columns.SubmittedTo is { } to)
        {
            var endExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            claims = claims.Where(claim => claim.SubmittedAt < endExclusive);
        }

        return claims;
    }

    private IQueryable<Claim> VisibleTo(Actor actor)
    {
        var claims = Claims();
        return actor.IsStaff ? claims : claims.Where(claim => claim.ClaimantId == actor.UserId);
    }

    private IQueryable<Claim> Filtered(ClaimListFilter filter)
    {
        var claims = Claims(filter.Columns.ClaimNumber);
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
