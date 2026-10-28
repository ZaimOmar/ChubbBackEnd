using System.Reflection;
using System.Text.Json;

namespace Chubb.Claims.Domain.Tests;

public sealed class ClaimantProjectionTests
{
    private readonly Scenario _scenario = new();

    [Fact]
    public void Project_FullLifecycleWithSendBack_ReturnsOnlyPublicItems()
    {
        var timeline = ClaimantTimeline.Project(LifecycleWithSendBackThenApproval());

        timeline.Select(item => item.Summary).Should().Equal(
            "Claim received: CLM-SG-2026-000123",
            "Claim picked up for review",
            "Decision under review",
            "Claim approved for SGD 7,500.00");
    }

    [Fact]
    public void Project_AfterSendBack_ShowsNoSendBackNoOutcomeNoStaffIdentity()
    {
        var (claim, proposalId) = _scenario.PendingApproval();
        claim.SendBackDecision(_scenario.As(_scenario.Manager), proposalId, "Please re-check the invoice").ShouldSucceed();

        var json = JsonSerializer.Serialize(ClaimantTimeline.Project(claim.DrainPending().Entries));

        json.Should().Contain("Decision under review")
            .And.NotContain("sent back").And.NotContain("Please re-check")
            .And.NotContain(_scenario.Officer.UserId.ToString()).And.NotContain(_scenario.Manager.UserId.ToString())
            .And.NotContain(proposalId.ToString()).And.NotContain("Assessed").And.NotContain("7,500");
    }

    [Fact]
    public void Project_DuringPendingProposal_ShowsOnlyDecisionUnderReview()
    {
        var (claim, _) = _scenario.PendingApproval();

        var timeline = ClaimantTimeline.Project(claim.DrainPending().Entries);

        timeline.Count(item => item.Summary == "Decision under review").Should().Be(1);
        timeline.Should().NotContain(item => item.Summary.Contains("Decision proposed"));
    }

    [Fact]
    public void Project_ShuffledInput_OrdersByOccurredAtThenId()
    {
        var entries = Enumerable.Reverse(LifecycleWithSendBackThenApproval()).ToList();

        var timeline = ClaimantTimeline.Project(entries);

        timeline.Should().BeInAscendingOrder(item => item.OccurredAt);
        timeline.Where(item => item.OccurredAt == Scenario.Now).Select(item => item.Id).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Project_OnlyInternalEntries_ReturnsEmpty()
    {
        var claim = _scenario.Assessed();
        var internalOnly = claim.DrainPending().Entries.Where(entry => entry.Visibility == TrailVisibility.Internal);

        ClaimantTimeline.Project(internalOnly).Should().BeEmpty();
    }

    [Fact]
    public void Project_ItemShape_ExposesOnlyIdTimeAndSummary()
    {
        var names = typeof(ClaimantTimelineItem).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);

        names.Should().BeEquivalentTo("Id", "OccurredAt", "Summary");
    }

    [Fact]
    public void Project_PublicEntryWithReserveFields_DoesNotExposeReserve()
    {
        var submitted = _scenario.Submitted(9_000m).DrainPending().Entries.Single();

        var json = JsonSerializer.Serialize(ClaimantTimeline.Project([submitted]));

        submitted.ReserveAfter.Should().Be(9_000m);
        json.Should().NotContain("9000").And.NotContain("Reserve");
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Submitted)]
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.InfoRequested, ClaimStatus.InfoRequested)]
    [InlineData(ClaimStatus.PendingDecisionApproval, ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.Approved, ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Rejected, ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.PendingSettlementApproval, ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Settling, ClaimStatus.Settling)]
    [InlineData(ClaimStatus.Settled, ClaimStatus.Settled)]
    public void StatusFor_EachStatus_MapsPendingToVisibleStatus(ClaimStatus status, ClaimStatus expected)
    {
        ClaimantView.StatusFor(status).Should().Be(expected);
    }

    private List<TrailEntry> LifecycleWithSendBackThenApproval()
    {
        var claim = _scenario.Submitted();
        var entries = new List<TrailEntry>(claim.DrainPending().Entries);
        claim.PickUp(_scenario.As(_scenario.Officer)).ShouldSucceed();
        claim.Assess(_scenario.As(_scenario.Officer), Money.Create(8_000m, "SGD"), "Inspected").ShouldSucceed();
        var first = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Repair cost verified")).Value;
        claim.SendBackDecision(_scenario.As(_scenario.Manager), first, "Please re-check the invoice").ShouldSucceed();
        var second = claim.ProposeDecision(_scenario.As(_scenario.Officer), Scenario.Approve("Invoice re-checked")).Value;
        claim.ConfirmDecision(_scenario.As(_scenario.Manager), second).ShouldSucceed();
        entries.AddRange(claim.DrainPending().Entries);
        return entries;
    }
}
