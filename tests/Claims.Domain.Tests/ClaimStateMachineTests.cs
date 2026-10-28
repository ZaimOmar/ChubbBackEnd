namespace Chubb.Claims.Domain.Tests;

public sealed class ClaimStateMachineTests
{
    private static readonly HashSet<(ClaimStatus, ClaimStatus)> PhaseOneEdges =
    [
        (ClaimStatus.Submitted, ClaimStatus.UnderReview),
        (ClaimStatus.UnderReview, ClaimStatus.InfoRequested),
        (ClaimStatus.InfoRequested, ClaimStatus.UnderReview),
        (ClaimStatus.UnderReview, ClaimStatus.PendingDecisionApproval),
        (ClaimStatus.PendingDecisionApproval, ClaimStatus.Approved),
        (ClaimStatus.PendingDecisionApproval, ClaimStatus.Rejected),
        (ClaimStatus.PendingDecisionApproval, ClaimStatus.UnderReview),
    ];

    private static readonly HashSet<(ClaimStatus, ClaimStatus)> PhaseTwoEdges =
    [
        (ClaimStatus.Approved, ClaimStatus.PendingSettlementApproval),
        (ClaimStatus.PendingSettlementApproval, ClaimStatus.Settling),
        (ClaimStatus.PendingSettlementApproval, ClaimStatus.Approved),
        (ClaimStatus.Settling, ClaimStatus.Settled),
        (ClaimStatus.Settling, ClaimStatus.Approved),
    ];

    private static readonly ClaimStatus[] PhaseTwoStatuses =
        [ClaimStatus.PendingSettlementApproval, ClaimStatus.Settling, ClaimStatus.Settled];

    public static TheoryData<ClaimStatus, ClaimStatus> AllPairs()
    {
        var pairs = new TheoryData<ClaimStatus, ClaimStatus>();
        foreach (var from in Enum.GetValues<ClaimStatus>())
        {
            foreach (var to in Enum.GetValues<ClaimStatus>())
            {
                pairs.Add(from, to);
            }
        }

        return pairs;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Evaluate_EveryStatusPairWithSettlementDisabled_MatchesTable(ClaimStatus from, ClaimStatus to)
    {
        var touchesPhaseTwo = PhaseTwoStatuses.Contains(from) || PhaseTwoStatuses.Contains(to);
        var expected = touchesPhaseTwo ? TransitionVerdict.Phase2NotEnabled
            : PhaseOneEdges.Contains((from, to)) ? TransitionVerdict.Allowed
            : TransitionVerdict.Invalid;

        ClaimStateMachine.Evaluate(from, to, settlementEnabled: false).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Evaluate_EveryStatusPairWithSettlementEnabled_MatchesTable(ClaimStatus from, ClaimStatus to)
    {
        var isEdge = PhaseOneEdges.Contains((from, to)) || PhaseTwoEdges.Contains((from, to));

        ClaimStateMachine.Evaluate(from, to, settlementEnabled: true)
            .Should().Be(isEdge ? TransitionVerdict.Allowed : TransitionVerdict.Invalid);
    }

    [Fact]
    public void Evaluate_SettlementDisabled_AllowsExactlyPhaseOneEdges()
    {
        var verdicts = Verdicts(settlementEnabled: false);

        verdicts.Count(verdict => verdict == TransitionVerdict.Allowed).Should().Be(7);
        verdicts.Count(verdict => verdict == TransitionVerdict.Phase2NotEnabled).Should().Be(45);
        verdicts.Count(verdict => verdict == TransitionVerdict.Invalid).Should().Be(29);
    }

    [Fact]
    public void Evaluate_SettlementEnabled_AllowsTwelveEdges()
    {
        var verdicts = Verdicts(settlementEnabled: true);

        verdicts.Count(verdict => verdict == TransitionVerdict.Allowed).Should().Be(12);
        verdicts.Count(verdict => verdict == TransitionVerdict.Invalid).Should().Be(69);
    }

    [Theory]
    [InlineData(ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Settled)]
    public void Evaluate_FromFinalStatus_AlwaysInvalid(ClaimStatus final)
    {
        var verdicts = Enum.GetValues<ClaimStatus>().Select(to => ClaimStateMachine.Evaluate(final, to, settlementEnabled: true));

        verdicts.Should().OnlyContain(verdict => verdict == TransitionVerdict.Invalid);
    }

    [Fact]
    public void Evaluate_SameStatus_InvalidTransition()
    {
        var verdicts = Enum.GetValues<ClaimStatus>().Select(status => ClaimStateMachine.Evaluate(status, status, settlementEnabled: true));

        verdicts.Should().OnlyContain(verdict => verdict == TransitionVerdict.Invalid);
    }

    private static List<TransitionVerdict> Verdicts(bool settlementEnabled) =>
        (from source in Enum.GetValues<ClaimStatus>()
         from target in Enum.GetValues<ClaimStatus>()
         select ClaimStateMachine.Evaluate(source, target, settlementEnabled)).ToList();
}
