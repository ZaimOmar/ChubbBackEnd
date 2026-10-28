namespace Chubb.Reporting.Application;

public sealed record PendingApprovalPageView(int Page, int PageSize, int TotalCount, IReadOnlyList<PendingApprovalView> Items);
