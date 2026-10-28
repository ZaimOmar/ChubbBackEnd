using System.Reflection;
using Chubb.Contracts;
using Chubb.Contracts.Events;

namespace Chubb.Claims.Domain.Tests;

public sealed class CheckerDecisionTests
{
    private readonly Scenario _scenario = new();

    [Fact]
    public void ConfirmDecision_ApproveByOtherManager_ApprovedReserveEqualsPayable()
    {
        var (claim, proposalId) = _scenario.PendingApproval(8_000m);

        claim.ConfirmDecision(_scenario.As(_scenario.Manager), proposalId).ShouldSucceed();

        claim.Status.Should().Be(ClaimStatus.Approved);
        claim.Payable.Should().Be(Money.Create(7_500m, "SGD"));
        claim.Reserve.Amount.Should().Be(7_500m);
        claim.Proposals.Single().State.Should().Be(ProposalState.Confirmed);
    }

    [Fact]
    public void ConfirmDecision_RejectByOtherManager_RejectedReserveZero()
    {
        var (claim, proposalId) = _scenario.PendingRejection();

        claim.ConfirmDecision(_scenario.As(_scenario.Manager), proposalId).ShouldSucceed();

        claim.Status.Should().Be(ClaimStatus.Rejected);
        claim.Reserve.Amount.Should().Be(0m);
        claim.Payable.Should().BeNull();
    }

    [Fact]
    public void ConfirmDecision_Approve_WritesInternalAndPublicEntriesWithMakerCheckerAndReserve()
    {
        var (claim, proposalId) = _scenario.PendingApproval(8_000m);
        claim.DrainPending();

        claim.ConfirmDecision(_scenario.As(_scenario.Manager), proposalId).ShouldSucceed();

        var (entries, events) = claim.DrainPending();
        var internalEntry = entries.Single(entry => entry.Visibility == TrailVisibility.Internal);
        internalEntry.MakerId.Should().Be(_scenario.Officer.UserId);
        internalEntry.CheckerId.Should().Be(_scenario.Manager.UserId);
        internalEntry.ProposalId.Should().Be(proposalId);
        internalEntry.ActorRole.Should().Be(Roles.Manager);
        internalEntry.ReserveBefore.Should().Be(7_500m);
        internalEntry.ReserveAfter.Should().Be(7_500m);
        entries.Single(entry => entry.Visibility == TrailVisibility.Public).Summary.Should().Be("Claim approved for SGD 7,500.00");
        events.Should().ContainSingle().Which.Should().Be(new ClaimApproved(
            "Approved", _scenario.Claimant.UserId, proposalId, _scenario.Officer.UserId, _scenario.Manager.UserId, 7_500m, "SGD", claim.Number.ToString()));
    }

    [Fact]
    public void ConfirmDecision_Reject_PublicEntryShowsCodeLabelAndTextAndReserveReleased()
    {
        var (claim, proposalId) = _scenario.PendingRejection();
        claim.DrainPending();

        claim.ConfirmDecision(_scenario.As(_scenario.Manager), proposalId).ShouldSucceed();

        var (entries, events) = claim.DrainPending();
        entries.Single(entry => entry.Visibility == TrailVisibility.Public).Summary
            .Should().Be("Claim rejected: Not covered. Your policy excludes flood damage.");
        entries.Single(entry => entry.Visibility == TrailVisibility.Internal).ReserveAfter.Should().Be(0m);
        events.Should().ContainSingle().Which.Should().Be(new ClaimRejected(
            "Rejected", _scenario.Claimant.UserId, proposalId, _scenario.Officer.UserId, _scenario.Manager.UserId, "NOT_COVERED", 10_000m, "SGD", claim.Number.ToString()));
    }

    [Fact]
    public void ConfirmDecision_MakerWhoAlsoHoldsManagerRole_MakerCannotCheckTrailedNoChange()
    {
        var claim = _scenario.Assessed();
        var proposalId = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Repair cost verified")).Value;
        var dualRoleMaker = _scenario.Officer with { Roles = _scenario.DualRole.Roles };
        claim.DrainPending();

        var result = claim.ConfirmDecision(_scenario.As(dualRoleMaker), proposalId);

        result.ShouldBeRefused(DomainRefusals.Types.MakerCannotCheck, trailed: true);
        claim.Status.Should().Be(ClaimStatus.PendingDecisionApproval);
        claim.Proposals.Single().State.Should().Be(ProposalState.Open);
        claim.DrainPending().Entries.Should().BeEmpty();
    }

    [Fact]
    public void ConfirmDecision_SameUserIdDifferentRolesSet_MakerCannotCheck()
    {
        var (claim, proposalId) = _scenario.PendingApproval();
        var sameUserAsManager = new Actor(_scenario.Officer.UserId, new HashSet<string> { Roles.Manager });

        claim.ConfirmDecision(_scenario.As(sameUserAsManager), proposalId)
            .ShouldBeRefused(DomainRefusals.Types.MakerCannotCheck, trailed: true);
    }

    [Fact]
    public void ConfirmDecision_DualRoleUserWhoIsNotTheMaker_MayConfirm()
    {
        var (claim, proposalId) = _scenario.PendingApproval();

        claim.ConfirmDecision(_scenario.As(_scenario.DualRole), proposalId).ShouldSucceed();
    }

    [Fact]
    public void ConfirmDecision_StaleProposalId_ProposalNotOpenNothingApplied()
    {
        var (claim, _) = _scenario.PendingApproval();
        claim.DrainPending();

        var result = claim.ConfirmDecision(_scenario.As(_scenario.Manager), Guid.NewGuid());

        result.ShouldBeRefused(DomainRefusals.Types.ProposalNotOpen, trailed: true);
        claim.Status.Should().Be(ClaimStatus.PendingDecisionApproval);
        claim.DrainPending().Entries.Should().BeEmpty();
    }

    [Fact]
    public void ConfirmDecision_StaleProposalIdAsMaker_ProposalNotOpenBeforeMakerCheck()
    {
        var (claim, _) = _scenario.PendingApprovalBy(_scenario.DualRole);

        claim.ConfirmDecision(_scenario.As(_scenario.DualRole), Guid.NewGuid())
            .ShouldBeRefused(DomainRefusals.Types.ProposalNotOpen, trailed: true);
    }

    [Fact]
    public void ConfirmDecision_SecondConfirmAfterFirst_ProposalNotOpen()
    {
        var (claim, proposalId) = _scenario.PendingApproval();
        claim.ConfirmDecision(_scenario.As(_scenario.Manager), proposalId).ShouldSucceed();

        claim.ConfirmDecision(_scenario.As(_scenario.Manager), proposalId)
            .ShouldBeRefused(DomainRefusals.Types.ProposalNotOpen, trailed: true);
    }

    [Fact]
    public void ConfirmDecision_ClaimNotPending_ProposalNotOpen()
    {
        var claim = _scenario.UnderReview();

        claim.ConfirmDecision(_scenario.As(_scenario.Manager), Guid.NewGuid())
            .ShouldBeRefused(DomainRefusals.Types.ProposalNotOpen, trailed: true);
    }

    [Fact]
    public void ConfirmDecision_Signature_TakesNoCallerSuppliedAmountOrOutcome()
    {
        var parameters = typeof(Claim).GetMethod(nameof(Claim.ConfirmDecision))?.GetParameters() ?? [];

        parameters.Select(parameter => parameter.ParameterType).Should().Equal(typeof(CommandContext), typeof(Guid));
    }

    [Fact]
    public void SendBackDecision_ByOtherManager_UnderReviewSameAssigneeReserveUnchanged()
    {
        var (claim, proposalId) = _scenario.PendingApproval(8_000m);

        claim.SendBackDecision(_scenario.As(_scenario.Manager), proposalId, "Please re-check the invoice").ShouldSucceed();

        claim.Status.Should().Be(ClaimStatus.UnderReview);
        claim.AssignedOfficerId.Should().Be(_scenario.Officer.UserId);
        claim.Reserve.Amount.Should().Be(7_500m);
        claim.Proposals.Single().State.Should().Be(ProposalState.SentBack);
    }

    [Fact]
    public void SendBackDecision_WritesInternalEntryOnlyAndRaisesEvent()
    {
        var (claim, proposalId) = _scenario.PendingApproval();
        claim.DrainPending();

        claim.SendBackDecision(_scenario.As(_scenario.Manager), proposalId, "Please re-check the invoice").ShouldSucceed();

        var (entries, events) = claim.DrainPending();
        var entry = entries.Should().ContainSingle().Subject;
        entry.Visibility.Should().Be(TrailVisibility.Internal);
        entry.Reason.Should().Be("Please re-check the invoice");
        entry.CheckerId.Should().Be(_scenario.Manager.UserId);
        events.Should().ContainSingle().Which.Should().Be(
            new DecisionSentBack("UnderReview", proposalId, _scenario.Officer.UserId, _scenario.Manager.UserId, claim.Number.ToString()));
    }

    [Fact]
    public void SendBackDecision_ByMaker_MakerCannotCheck()
    {
        var (claim, proposalId) = _scenario.PendingApprovalBy(_scenario.DualRole);

        claim.SendBackDecision(_scenario.As(_scenario.DualRole), proposalId, "Please re-check the invoice")
            .ShouldBeRefused(DomainRefusals.Types.MakerCannotCheck, trailed: true);
    }

    [Fact]
    public void SendBackDecision_ReasonUnderTenCharacters_ValidationError()
    {
        var (claim, proposalId) = _scenario.PendingApproval();

        claim.SendBackDecision(_scenario.As(_scenario.Manager), proposalId, "no")
            .ShouldBeRefused(DomainRefusals.Types.ValidationError, trailed: false);
        claim.Status.Should().Be(ClaimStatus.PendingDecisionApproval);
    }

    [Fact]
    public void SendBackDecision_ThenReproposeByMaker_Succeeds()
    {
        var (claim, proposalId) = _scenario.PendingApproval();
        claim.SendBackDecision(_scenario.As(_scenario.Manager), proposalId, "Please re-check the invoice").ShouldSucceed();

        claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Invoice re-checked")).IsSuccess.Should().BeTrue();

        claim.Proposals.Should().HaveCount(2);
    }

    [Fact]
    public void Commands_FullLifecycleWithSendBack_AddExpectedEntryCounts()
    {
        var claim = _scenario.Submitted();
        var counts = new List<int> { claim.DrainPending().Entries.Count };

        claim.PickUp(_scenario.As(_scenario.Officer)).ShouldSucceed();
        counts.Add(claim.DrainPending().Entries.Count);
        claim.Assess(_scenario.As(_scenario.Officer), Money.Create(8_000m, "SGD"), string.Empty).ShouldSucceed();
        counts.Add(claim.DrainPending().Entries.Count);
        var first = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Repair cost verified")).Value;
        counts.Add(claim.DrainPending().Entries.Count);
        claim.SendBackDecision(_scenario.As(_scenario.Manager), first, "Please re-check the invoice").ShouldSucceed();
        counts.Add(claim.DrainPending().Entries.Count);
        var second = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Invoice re-checked")).Value;
        counts.Add(claim.DrainPending().Entries.Count);
        claim.ConfirmDecision(_scenario.As(_scenario.Manager), second).ShouldSucceed();
        counts.Add(claim.DrainPending().Entries.Count);

        counts.Should().Equal(1, 1, 1, 2, 1, 1, 2);
    }

    [Fact]
    public void Reserve_ThroughFullHappyPath_FollowsSubmitAssessApproveRules()
    {
        var claim = _scenario.Submitted(10_000m);
        claim.Reserve.Amount.Should().Be(10_000m);

        claim.PickUp(_scenario.As(_scenario.Officer)).ShouldSucceed();
        claim.Reserve.Amount.Should().Be(10_000m);

        claim.Assess(_scenario.As(_scenario.Officer), Money.Create(8_000m, "SGD"), string.Empty).ShouldSucceed();
        claim.Reserve.Amount.Should().Be(7_500m);

        var proposalId = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Repair cost verified")).Value;
        claim.Reserve.Amount.Should().Be(7_500m);

        claim.ConfirmDecision(_scenario.As(_scenario.Manager), proposalId).ShouldSucceed();
        claim.Reserve.Amount.Should().Be(7_500m);
    }

    [Fact]
    public void Reserve_ProposeAndSendBack_NeverChangesReserveOrPayable()
    {
        var (claim, proposalId) = _scenario.PendingApproval(8_000m);
        claim.SendBackDecision(_scenario.As(_scenario.Manager), proposalId, "Please re-check the invoice").ShouldSucceed();

        claim.Reserve.Amount.Should().Be(7_500m);
        claim.Payable.Should().BeNull();
    }

    [Fact]
    public void PendingClaim_RefusedCommands_LeaveStateUnchanged()
    {
        var (claim, proposalId) = _scenario.PendingApprovalBy(_scenario.DualRole);
        claim.DrainPending();
        var before = Snapshot(claim);

        claim.Assess(_scenario.As(_scenario.DualRole), Money.Create(1m, "SGD"), string.Empty).ShouldBeRefused(DomainRefusals.Types.ProposalPending, true);
        claim.ProposeDecision(_scenario.As(_scenario.DualRole), Scenario.Approve("Repair cost verified")).ShouldBeRefused(DomainRefusals.Types.ProposalPending, true);
        claim.PickUp(_scenario.As(_scenario.OtherOfficer)).ShouldBeRefused(DomainRefusals.Types.InvalidTransition, true);
        claim.ConfirmDecision(_scenario.As(_scenario.DualRole), proposalId).ShouldBeRefused(DomainRefusals.Types.MakerCannotCheck, true);

        Snapshot(claim).Should().Be(before);
        claim.DrainPending().Entries.Should().BeEmpty();
    }

    [Fact]
    public void Claim_PublicMembers_HaveNoPublicSetters()
    {
        var setters = typeof(Claim).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true });

        setters.Should().BeEmpty();
    }

    private static string Snapshot(Claim claim) =>
        $"{claim.Status}|{claim.Reserve}|{claim.AssignedOfficerId}|{claim.UpdatedAt:O}|{claim.Assessed}|{claim.NetPayable}|{claim.Payable}|"
        + string.Join(',', claim.Proposals.Select(proposal => $"{proposal.Id}:{proposal.State}"));
}
