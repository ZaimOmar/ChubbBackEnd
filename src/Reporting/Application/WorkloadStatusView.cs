namespace Chubb.Reporting.Application;

public sealed record WorkloadStatusView(string Status, bool WaitingOnChecker, int Count, int OldestAgeDays);
