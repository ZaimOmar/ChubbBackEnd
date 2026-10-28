namespace Chubb.Reporting.Application;

public sealed record OutstandingView(
    int Total,
    int AwaitingChecker,
    IReadOnlyList<CountView> ByStatus,
    IReadOnlyList<CountView> ByMarket,
    IReadOnlyList<CountView> ByType,
    int UnassignedCount,
    OldestUnassignedView? OldestUnassigned);
