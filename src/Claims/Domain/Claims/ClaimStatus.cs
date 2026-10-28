namespace Chubb.Claims.Domain;

// Persisted as strings; never reorder or rename.
public enum ClaimStatus
{
    Submitted,
    UnderReview,
    InfoRequested,
    PendingDecisionApproval,
    Approved,
    Rejected,
    PendingSettlementApproval,
    Settling,
    Settled,
}
