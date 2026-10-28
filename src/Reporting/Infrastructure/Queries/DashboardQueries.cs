using Chubb.Reporting.Application;
using Chubb.Reporting.Domain;
using Chubb.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Reporting.Infrastructure.Queries;

public sealed class DashboardQueries(ReportingDbContext db, TimeProvider clock)
{
    private const string UsdCurrency = "USD";

    public async Task<OutstandingView> GetOutstandingAsync(CancellationToken cancellationToken)
    {
        var byStatus = await CountOpenBy(claim => claim.Status, cancellationToken);
        var byMarket = await CountOpenBy(claim => claim.Market, cancellationToken);
        var byType = await CountOpenBy(claim => claim.Type, cancellationToken);
        var statusRows = ClaimStatuses.Open.Select(status => new CountView(status, byStatus.GetValueOrDefault(status))).ToList();
        var unassigned = db.ClaimSnapshots.AsNoTracking()
            .Where(claim => claim.Status == ClaimStatuses.Submitted && claim.AssigneeId == null);

        return new OutstandingView(
            statusRows.Sum(row => row.Count),
            statusRows.Where(row => ClaimStatuses.IsAwaitingChecker(row.Key)).Sum(row => row.Count),
            statusRows,
            ToRows(byMarket),
            ToRows(byType),
            await unassigned.CountAsync(cancellationToken),
            await FindOldestAsync(unassigned, cancellationToken));
    }

    public async Task<ExposureView> GetExposureAsync(CancellationToken cancellationToken)
    {
        var groups = await db.ClaimSnapshots.AsNoTracking()
            .Where(claim => !ClaimStatuses.Closed.Contains(claim.Status))
            .GroupBy(claim => new { claim.Market, claim.Type, claim.Currency })
            .Select(group => new { group.Key.Market, group.Key.Type, group.Key.Currency, Reserve = group.Sum(claim => claim.Reserve), Count = group.Count() })
            .ToListAsync(cancellationToken);
        var rows = groups
            .GroupBy(group => (group.Market, group.Type))
            .Select(pair => new ExposureRowView(
                pair.Key.Market, pair.Key.Type, pair.Sum(group => group.Count), pair.Sum(group => FxRates.ToUsd(group.Reserve, group.Currency))))
            .OrderBy(row => row.Market).ThenBy(row => row.Type)
            .ToList();

        return new ExposureView(UsdCurrency, FxRates.AsOf, rows.Sum(row => row.ExposureUsd), rows);
    }

    public async Task<PendingApprovalPageView> GetPendingApprovalsAsync(PageRequest request, CancellationToken cancellationToken)
    {
        var queue = db.PendingProposals.AsNoTracking()
            .Join(db.ClaimSnapshots, proposal => proposal.ClaimId, claim => claim.ClaimId, (proposal, claim) => new { proposal, claim })
            .Where(pair => ClaimStatuses.AwaitingChecker.Contains(pair.claim.Status));
        var page = await queue
            .OrderBy(pair => pair.proposal.ProposedAt).ThenBy(pair => pair.claim.Number)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(pair => new
            {
                pair.claim.ClaimId,
                pair.claim.Number,
                pair.claim.Market,
                pair.claim.Type,
                pair.proposal.Amount,
                pair.proposal.Currency,
                pair.proposal.MakerId,
                pair.claim.AssigneeName,
                pair.proposal.ProposedAt,
            })
            .ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var items = page
            .Select(row => new PendingApprovalView(
                row.ClaimId, row.Number, row.Market, row.Type, row.Amount, row.Currency, row.MakerId, row.AssigneeName ?? row.MakerId.ToString("N")[..8], row.ProposedAt, AgeHours(now, row.ProposedAt)))
            .ToList();

        return new PendingApprovalPageView(request.Page, request.PageSize, await queue.CountAsync(cancellationToken), items);
    }

    private async Task<Dictionary<string, int>> CountOpenBy(
        System.Linq.Expressions.Expression<Func<ClaimSnapshot, string>> key, CancellationToken cancellationToken) =>
        await db.ClaimSnapshots.AsNoTracking()
            .Where(claim => !ClaimStatuses.Closed.Contains(claim.Status))
            .GroupBy(key)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);

    private async Task<OldestUnassignedView?> FindOldestAsync(IQueryable<ClaimSnapshot> unassigned, CancellationToken cancellationToken)
    {
        var oldest = await unassigned.OrderBy(claim => claim.SubmittedAt).ThenBy(claim => claim.Number).FirstOrDefaultAsync(cancellationToken);
        return oldest is null
            ? null
            : new OldestUnassignedView(
                oldest.ClaimId, oldest.Number, oldest.Market, oldest.Type, oldest.SubmittedAt, AgeHours(clock.GetUtcNow(), oldest.SubmittedAt));
    }

    private static List<CountView> ToRows(Dictionary<string, int> counts) =>
        counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new CountView(pair.Key, pair.Value)).ToList();

    private static double AgeHours(DateTimeOffset now, DateTimeOffset since) => Math.Round((now - since).TotalHours, 1);
}
