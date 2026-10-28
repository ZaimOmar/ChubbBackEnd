namespace Chubb.Reporting.Application;

public sealed record OfficerWorkloadView(Guid OfficerId, string OfficerName, int OpenCount, IReadOnlyList<WorkloadStatusView> ByStatus);
