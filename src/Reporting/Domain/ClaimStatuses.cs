namespace Chubb.Reporting.Domain;

// Statuses arrive as the names the Claims state machine publishes; Reporting only classifies them.
public static class ClaimStatuses
{
    public const string Submitted = "Submitted";
    public const string UnderReview = "UnderReview";
    public const string InfoRequested = "InfoRequested";
    public const string PendingDecisionApproval = "PendingDecisionApproval";
    public const string Approved = "Approved";
    public const string PendingSettlementApproval = "PendingSettlementApproval";
    public const string Settling = "Settling";
    public const string Rejected = "Rejected";
    public const string Settled = "Settled";

    public static readonly IReadOnlyList<string> Open =
    [
        Submitted,
        UnderReview,
        InfoRequested,
        PendingDecisionApproval,
        Approved,
        PendingSettlementApproval,
        Settling,
    ];

    public static readonly IReadOnlyList<string> AwaitingChecker = [PendingDecisionApproval, PendingSettlementApproval];

    public static readonly IReadOnlyList<string> Closed = [Rejected, Settled];

    public static bool IsAwaitingChecker(string status) => AwaitingChecker.Contains(status);
}
