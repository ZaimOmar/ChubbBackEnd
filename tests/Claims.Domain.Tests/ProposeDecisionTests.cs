using Chubb.Contracts;
using Chubb.Contracts.Events;

namespace Chubb.Claims.Domain.Tests;

public sealed class ProposeDecisionTests
{
    private readonly Scenario _scenario = new();

    [Fact]
    public void ProposeDecision_ApproveWithAssessedAmount_PendingWithInternalAndPublicEntries()
    {
        var claim = _scenario.Assessed(8_000m);
        claim.DrainPending();

        var result = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Repair cost verified"));

        result.IsSuccess.Should().BeTrue();
        claim.Status.Should().Be(ClaimStatus.PendingDecisionApproval);
        var (entries, events) = claim.DrainPending();
        entries.Select(entry => entry.EventType).Should().Equal(TrailEntryType.DecisionProposed, TrailEntryType.DecisionUnderReview);
        entries.Select(entry => entry.Visibility).Should().Equal(TrailVisibility.Internal, TrailVisibility.Public);
        events.Should().ContainSingle().Which.Should().Be(
            new DecisionProposed("PendingDecisionApproval", result.Value, _scenario.Officer.UserId, "Approve", 7_500m, "SGD", claim.Number.ToString()));
    }

    [Fact]
    public void ProposeDecision_Success_ReserveAndPayableUnchanged()
    {
        var (claim, _) = _scenario.PendingApproval(8_000m);

        claim.Reserve.Amount.Should().Be(7_500m);
        claim.Payable.Should().BeNull();
        claim.AssignedOfficerId.Should().Be(_scenario.Officer.UserId);
    }

    [Theory]
    [InlineData(RejectReasonCode.NotCovered)]
    [InlineData(RejectReasonCode.PolicyInactive)]
    [InlineData(RejectReasonCode.ExclusionApplies)]
    [InlineData(RejectReasonCode.InsufficientEvidence)]
    [InlineData(RejectReasonCode.DuplicateClaim)]
    [InlineData(RejectReasonCode.NoResponse)]
    public void ProposeDecision_RejectWithListedCode_Succeeds(RejectReasonCode code)
    {
        var claim = _scenario.UnderReview();

        var result = claim.ProposeDecision(_scenario.As(_scenario.Officer), new(ProposalOutcome.Reject, code, "Policy terms exclude this", null, "Your policy excludes this loss."));

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData((RejectReasonCode)99)]
    public void ProposeDecision_RejectWithMissingOrUnknownCode_ReasonCodeInvalid(RejectReasonCode? code)
    {
        var claim = _scenario.UnderReview();

        var result = claim.ProposeDecision(_scenario.As(_scenario.Officer), new(ProposalOutcome.Reject, code, "Policy terms exclude this", null));

        result.ShouldBeRefused(DomainRefusals.Types.ReasonCodeInvalid, trailed: false);
    }

    [Fact]
    public void ProposeDecision_RationaleUnderTenCharacters_ValidationError()
    {
        var claim = _scenario.Assessed();

        var result = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("too short"));

        result.ShouldBeRefused(DomainRefusals.Types.ValidationError, trailed: false);
    }

    [Fact]
    public void ProposeDecision_ApproveWithoutAssessedAmount_AssessedAmountRequired()
    {
        var claim = _scenario.UnderReview();

        var result = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Looks fine to me"));

        result.ShouldBeRefused(DomainRefusals.Types.AssessedAmountRequired, trailed: false);
    }

    [Fact]
    public void ProposeDecision_ApproveWithPayableZero_PayableZero()
    {
        var claim = _scenario.Assessed(400m);

        var result = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Below the excess"));

        result.ShouldBeRefused(DomainRefusals.Types.PayableZero, trailed: false);
    }

    [Fact]
    public void ProposeDecision_PayableAboveEstimateWithoutJustification_JustificationRequired()
    {
        var claim = _scenario.Assessed(assessed: 9_000m, estimate: 5_000m);

        var result = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Estimate was too low"));

        result.ShouldBeRefused(DomainRefusals.Types.JustificationRequired, trailed: false);
    }

    [Fact]
    public void ProposeDecision_PayableAboveEstimateWithJustification_Succeeds()
    {
        var claim = _scenario.Assessed(assessed: 9_000m, estimate: 5_000m);

        var result = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Estimate was too low", "Garage invoice"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ProposeDecision_RejectWithoutAssessedAmount_Succeeds()
    {
        var (claim, _) = _scenario.PendingRejection();

        claim.Status.Should().Be(ClaimStatus.PendingDecisionApproval);
        claim.Proposals.Single().Amount.Should().BeNull();
    }

    [Fact]
    public void ProposeDecision_NotAssignedOfficer_NotAssignedOfficerTrailed()
    {
        var claim = _scenario.Assessed();

        var result = claim.ProposeDecision(_scenario.As(_scenario.OtherOfficer), Scenario.Approve("Repair cost verified"));

        result.ShouldBeRefused(DomainRefusals.Types.NotAssignedOfficer, trailed: true);
    }

    [Fact]
    public void ProposeDecision_WhilePending_ProposalPending()
    {
        var (claim, _) = _scenario.PendingApproval();

        var result = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Repair cost verified"));

        result.ShouldBeRefused(DomainRefusals.Types.ProposalPending, trailed: true);
    }

    [Fact]
    public void ProposeDecision_SubmittedClaim_RefusedBecauseNobodyIsAssigned()
    {
        var claim = _scenario.Submitted();

        var result = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Repair cost verified"));

        result.ShouldBeRefused(DomainRefusals.Types.NotAssignedOfficer, trailed: true);
    }

    [Fact]
    public void ProposeDecision_PublicEntry_HasNoOutcomeAmountOrNames()
    {
        var claim = _scenario.Assessed();
        claim.DrainPending();
        claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Repair cost verified")).ShouldSucceed();

        var publicEntry = claim.DrainPending().Entries.Single(entry => entry.Visibility == TrailVisibility.Public);

        publicEntry.Summary.Should().Be("Decision under review");
        publicEntry.MakerId.Should().BeNull();
        publicEntry.Reason.Should().BeNull();
        publicEntry.FromStatus.Should().BeNull();
        publicEntry.ToStatus.Should().BeNull();
    }

    [Fact]
    public void ProposeDecision_Entries_ShareProposalIdAndOccurredAt()
    {
        var claim = _scenario.Assessed();
        claim.DrainPending();
        var proposalId = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Repair cost verified")).Value;

        var entries = claim.DrainPending().Entries;

        entries.Should().OnlyContain(entry => entry.ProposalId == proposalId && entry.OccurredAt == Scenario.Now);
        entries.Should().OnlyContain(entry => entry.ActorRole == Roles.Officer);
    }

    [Fact]
    public void ProposeDecision_SecondProposalAfterSendBack_InternalOnly()
    {
        var (claim, proposalId) = _scenario.PendingApproval();
        claim.SendBackDecision(_scenario.As(_scenario.Manager), proposalId, "Please re-check the invoice").ShouldSucceed();
        claim.DrainPending();

        claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Invoice re-checked")).ShouldSucceed();

        var entry = claim.DrainPending().Entries.Should().ContainSingle().Subject;
        entry.Visibility.Should().Be(TrailVisibility.Internal);
    }

    [Fact]
    public void ProposeDecision_FailedCommand_LeavesNoPendingEntriesOrEvents()
    {
        var claim = _scenario.Assessed();
        claim.DrainPending();

        claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("short")).ShouldBeRefused(DomainRefusals.Types.ValidationError, false);

        var (entries, events) = claim.DrainPending();
        entries.Should().BeEmpty();
        events.Should().BeEmpty();
        claim.Status.Should().Be(ClaimStatus.UnderReview);
        claim.Proposals.Should().BeEmpty();
    }
}
