using Chubb.Reporting.Application;
using Chubb.Reporting.Domain;
using Chubb.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Reporting.Infrastructure.Queries;

public sealed class WorkloadQueries(ReportingDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<OfficerWorkloadView>> GetWorkloadAsync(Guid? officerId, CancellationToken cancellationToken)
    {
        var open = db.ClaimSnapshots.AsNoTracking()
            .Where(claim => claim.AssigneeId != null && !ClaimStatuses.Closed.Contains(claim.Status));
        if (officerId is not null)
        {
            open = open.Where(claim => claim.AssigneeId == officerId);
        }

        var groups = await open
            .GroupBy(claim => new { claim.AssigneeId, claim.Status })
            .Select(group => new { group.Key.AssigneeId, group.Key.Status, Count = group.Count(), Oldest = group.Min(claim => claim.SubmittedAt), Name = group.Max(claim => claim.AssigneeName) })
            .ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();

        return groups
            .GroupBy(group => group.AssigneeId!.Value)
            .Select(officer => new OfficerWorkloadView(
                officer.Key,
                officer.Select(group => group.Name).FirstOrDefault(name => name is not null) ?? ShortId(officer.Key),
                officer.Sum(group => group.Count),
                officer.Select(group => new WorkloadStatusView(
                    group.Status, ClaimStatuses.IsAwaitingChecker(group.Status), group.Count, (int)(now - group.Oldest).TotalDays)).ToList()))
            .OrderBy(workload => workload.OfficerId)
            .ToList();
    }

    private static string ShortId(Guid id) => id.ToString("N")[..8];
}
