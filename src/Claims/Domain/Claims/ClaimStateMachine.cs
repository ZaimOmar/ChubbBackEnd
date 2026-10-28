using System.Collections.Frozen;

namespace Chubb.Claims.Domain;

public static class ClaimStateMachine
{
    private static readonly FrozenSet<(ClaimStatus From, ClaimStatus To)> PhaseOneEdges = new[]
    {
        (ClaimStatus.Submitted, ClaimStatus.UnderReview),
        (ClaimStatus.UnderReview, ClaimStatus.InfoRequested),
        (ClaimStatus.InfoRequested, ClaimStatus.UnderReview),
        (ClaimStatus.UnderReview, ClaimStatus.PendingDecisionApproval),
        (ClaimStatus.PendingDecisionApproval, ClaimStatus.Approved),
        (ClaimStatus.PendingDecisionApproval, ClaimStatus.Rejected),
        (ClaimStatus.PendingDecisionApproval, ClaimStatus.UnderReview),
    }.ToFrozenSet();

    private static readonly FrozenSet<(ClaimStatus From, ClaimStatus To)> PhaseTwoEdges = new[]
    {
        (ClaimStatus.Approved, ClaimStatus.PendingSettlementApproval),
        (ClaimStatus.PendingSettlementApproval, ClaimStatus.Settling),
        (ClaimStatus.PendingSettlementApproval, ClaimStatus.Approved),
        (ClaimStatus.Settling, ClaimStatus.Settled),
        (ClaimStatus.Settling, ClaimStatus.Approved),
    }.ToFrozenSet();

    // Phase 2 is checked first so a settlement attempt in the wrong state still reports "not built" (BR-04).
    public static TransitionVerdict Evaluate(ClaimStatus from, ClaimStatus to, bool settlementEnabled)
    {
        if (!settlementEnabled && (IsPhaseTwo(from) || IsPhaseTwo(to)))
        {
            return TransitionVerdict.Phase2NotEnabled;
        }

        var isEdge = PhaseOneEdges.Contains((from, to)) || (settlementEnabled && PhaseTwoEdges.Contains((from, to)));
        return isEdge ? TransitionVerdict.Allowed : TransitionVerdict.Invalid;
    }

    private static bool IsPhaseTwo(ClaimStatus status) =>
        status is ClaimStatus.PendingSettlementApproval or ClaimStatus.Settling or ClaimStatus.Settled;
}
