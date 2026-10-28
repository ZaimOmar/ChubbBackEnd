namespace Chubb.Claims.Domain;

public static class ClaimantView
{
    // A send-back must never show to the claimant as a status flip (BR-33).
    public static ClaimStatus StatusFor(ClaimStatus status) => status switch
    {
        ClaimStatus.PendingDecisionApproval => ClaimStatus.UnderReview,
        ClaimStatus.PendingSettlementApproval => ClaimStatus.Approved,
        _ => status,
    };
}
