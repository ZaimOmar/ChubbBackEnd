using Chubb.Contracts;
using Chubb.Contracts.Events;

namespace Chubb.Claims.Domain.Tests;

public sealed class PickUpAndAssessTests
{
    private readonly Scenario _scenario = new();

    [Fact]
    public void PickUp_Submitted_AssignsActorAndWritesPublicEntry()
    {
        var claim = _scenario.Submitted();
        claim.DrainPending();

        claim.PickUp(_scenario.As(_scenario.Officer)).ShouldSucceed();

        claim.Status.Should().Be(ClaimStatus.UnderReview);
        claim.AssignedOfficerId.Should().Be(_scenario.Officer.UserId);
        var (entries, events) = claim.DrainPending();
        var entry = entries.Should().ContainSingle().Subject;
        entry.Visibility.Should().Be(TrailVisibility.Public);
        entry.Summary.Should().Be("Claim picked up for review");
        entry.FromStatus.Should().Be(ClaimStatus.Submitted);
        entry.ToStatus.Should().Be(ClaimStatus.UnderReview);
        events.Should().ContainSingle().Which.Should().Be(
            new ClaimAssigned("UnderReview", _scenario.Claimant.UserId, _scenario.Officer.UserId, claim.Number.ToString(), null));
    }

    [Theory]
    [InlineData(ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Rejected)]
    public void PickUp_AnyOtherStatus_InvalidTransitionTrailed(ClaimStatus status)
    {
        var claim = ClaimIn(status);

        var result = claim.PickUp(_scenario.As(_scenario.OtherOfficer));

        result.ShouldBeRefused(DomainRefusals.Types.InvalidTransition, trailed: true);
    }

    [Fact]
    public void PickUp_AfterSuccess_SecondOfficerGetsInvalidTransitionAndKeepsFirstAssignee()
    {
        var claim = _scenario.UnderReview();

        var result = claim.PickUp(_scenario.As(_scenario.OtherOfficer));

        result.ShouldBeRefused(DomainRefusals.Types.InvalidTransition, trailed: true);
        claim.AssignedOfficerId.Should().Be(_scenario.Officer.UserId);
    }

    [Fact]
    public void Assess_AssessedEightThousandExcessFiveHundred_SetsPayableAndReserveSevenThousandFiveHundred()
    {
        var claim = _scenario.Assessed(8_000m);

        claim.Assessed.Should().Be(Money.Create(8_000m, "SGD"));
        claim.NetPayable.Should().Be(Money.Create(7_500m, "SGD"));
        claim.Reserve.Amount.Should().Be(7_500m);
        claim.Status.Should().Be(ClaimStatus.UnderReview);
    }

    [Fact]
    public void Assess_AboveCoverageLimit_ReservePayableCappedAtLimit()
    {
        var claim = _scenario.Assessed(20_000m);

        claim.NetPayable.Should().Be(Money.Create(10_000m, "SGD"));
        claim.Reserve.Amount.Should().Be(10_000m);
    }

    [Fact]
    public void Assess_Success_WritesInternalEntryWithReserveChangeAndRaisesEvent()
    {
        var claim = _scenario.UnderReview();
        claim.DrainPending();

        claim.Assess(_scenario.As(_scenario.Officer), Money.Create(8_000m, "SGD"), "Inspected").ShouldSucceed();

        var (entries, events) = claim.DrainPending();
        var entry = entries.Should().ContainSingle().Subject;
        entry.Visibility.Should().Be(TrailVisibility.Internal);
        entry.ActorRole.Should().Be(Roles.Officer);
        entry.ReserveBefore.Should().Be(10_000m);
        entry.ReserveAfter.Should().Be(7_500m);
        entry.Summary.Should().Be("Assessed amount set to 8,000.00 (excess 500.00, payable 7,500.00, reserve 10,000.00 to 7,500.00)");
        events.Should().ContainSingle().Which.Should().Be(new ClaimAssessed("UnderReview", 8_000m, 500m, 7_500m, 10_000m, 7_500m, "SGD"));
    }

    [Fact]
    public void Assess_RepeatedLowerThenHigher_FollowsLatestNetPayable()
    {
        var claim = _scenario.Assessed(8_000m);

        claim.Assess(_scenario.As(_scenario.Officer), Money.Create(400m, "SGD"), string.Empty).ShouldSucceed();
        claim.Reserve.Amount.Should().Be(0m);

        claim.Assess(_scenario.As(_scenario.Officer), Money.Create(9_000m, "SGD"), string.Empty).ShouldSucceed();
        claim.Reserve.Amount.Should().Be(8_500m);
    }

    [Fact]
    public void Assess_NotAssignedOfficer_NotAssignedOfficerTrailed()
    {
        var claim = _scenario.UnderReview();

        var result = claim.Assess(_scenario.As(_scenario.OtherOfficer), Money.Create(1m, "SGD"), string.Empty);

        result.ShouldBeRefused(DomainRefusals.Types.NotAssignedOfficer, trailed: true);
    }

    [Fact]
    public void Assess_WhilePending_ProposalPendingTrailed()
    {
        var (claim, _) = _scenario.PendingApproval();

        var result = claim.Assess(_scenario.As(_scenario.Officer), Money.Create(1m, "SGD"), string.Empty);

        result.ShouldBeRefused(DomainRefusals.Types.ProposalPending, trailed: true);
    }

    [Theory]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Rejected)]
    public void Assess_FinalStatus_InvalidTransition(ClaimStatus status)
    {
        var claim = ClaimIn(status);

        var result = claim.Assess(_scenario.As(_scenario.Officer), Money.Create(1m, "SGD"), string.Empty);

        result.ShouldBeRefused(DomainRefusals.Types.InvalidTransition, trailed: true);
    }

    [Fact]
    public void Assess_Zero_ValidationErrorAndNothingChanges()
    {
        var claim = _scenario.UnderReview();
        claim.DrainPending();

        var result = claim.Assess(_scenario.As(_scenario.Officer), Money.Zero("SGD"), string.Empty);

        result.ShouldBeRefused(DomainRefusals.Types.ValidationError, trailed: false);
        claim.Assessed.Should().BeNull();
        claim.DrainPending().Entries.Should().BeEmpty();
    }

    [Fact]
    public void Assess_WrongCurrency_CurrencyMismatch()
    {
        var claim = _scenario.UnderReview();

        var result = claim.Assess(_scenario.As(_scenario.Officer), Money.Create(1m, "HKD"), string.Empty);

        result.ShouldBeRefused(DomainRefusals.Types.CurrencyMismatch, trailed: false);
    }

    [Fact]
    public void DrainPending_SecondCall_ReturnsEmpty()
    {
        var claim = _scenario.Submitted();

        claim.DrainPending().Entries.Should().HaveCount(1);
        var second = claim.DrainPending();

        second.Entries.Should().BeEmpty();
        second.Events.Should().BeEmpty();
    }

    private Claim ClaimIn(ClaimStatus status)
    {
        if (status == ClaimStatus.UnderReview)
        {
            return _scenario.UnderReview();
        }

        if (status == ClaimStatus.Approved)
        {
            var (approved, approvedProposal) = _scenario.PendingApproval();
            approved.ConfirmDecision(_scenario.As(_scenario.Manager), approvedProposal).ShouldSucceed();
            return approved;
        }

        var (rejected, rejectedProposal) = _scenario.PendingRejection();
        rejected.ConfirmDecision(_scenario.As(_scenario.Manager), rejectedProposal).ShouldSucceed();
        return rejected;
    }
}
