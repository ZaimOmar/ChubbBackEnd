using Chubb.Contracts;

namespace Chubb.Claims.Domain.Tests;

internal sealed class Scenario
{
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly IncidentDate = new(2026, 6, 10);

    public Scenario()
    {
        Claimant = ActorWith(Roles.Claimant);
        Officer = ActorWith(Roles.Officer);
        OtherOfficer = ActorWith(Roles.Officer);
        Manager = ActorWith(Roles.Manager);
        DualRole = ActorWith(Roles.Officer, Roles.Manager);
        Policy = PolicyFor(Claimant.UserId, Markets.Singapore, 10_000m, 500m);
    }

    public TestIdGenerator Ids { get; } = new();

    public Actor Claimant { get; }

    public Actor Officer { get; }

    public Actor OtherOfficer { get; }

    public Actor Manager { get; }

    public Actor DualRole { get; }

    public Policy Policy { get; }

    public static Actor ActorWith(params string[] roles) => new(Guid.NewGuid(), roles.ToHashSet());

    public static Policy PolicyFor(Guid holderId, string market, decimal limit, decimal excess)
    {
        var currency = MarketCurrency.For(market);
        return new Policy
        {
            Id = Guid.NewGuid(),
            Number = "POL-0001",
            PolicyholderId = holderId,
            Market = market,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            Excess = Money.Create(excess, currency),
            CoverageLimit = Money.Create(limit, currency),
        };
    }

    public CommandContext As(Actor actor, DateTimeOffset? now = null) =>
        new(actor, now ?? Now, Guid.NewGuid(), Ids);

    public NewClaim NewClaimRequest(decimal estimate = 10_000m) => new(
        ClaimNumber.Create(Policy.Market, 2026, 123),
        Policy.Market,
        IncidentDate,
        "Rear-ended at a junction",
        Money.Create(estimate, Policy.CoverageLimit.Currency),
        new MotorDetails("SGX1234A", "Orchard Road"));

    public Claim Submitted(decimal estimate = 10_000m) =>
        Claim.Submit(As(Claimant), NewClaimRequest(estimate), Policy).Value;

    public Claim UnderReview(decimal estimate = 10_000m)
    {
        var claim = Submitted(estimate);
        claim.PickUp(As(Officer)).ShouldSucceed();
        return claim;
    }

    public Claim Assessed(decimal assessed = 8_000m, decimal estimate = 10_000m)
    {
        var claim = UnderReview(estimate);
        claim.Assess(As(Officer), Money.Create(assessed, claim.Currency), "Inspected").ShouldSucceed();
        return claim;
    }

    public (Claim Claim, Guid ProposalId) PendingApproval(decimal assessed = 8_000m)
    {
        var claim = Assessed(assessed);
        var proposalId = claim.ProposeDecision(As(Officer), Approve("Repair cost verified")).Value;
        return (claim, proposalId);
    }

    public (Claim Claim, Guid ProposalId) PendingApprovalBy(Actor maker)
    {
        var claim = Submitted();
        claim.PickUp(As(maker)).ShouldSucceed();
        claim.Assess(As(maker), Money.Create(8_000m, claim.Currency), "Inspected").ShouldSucceed();
        var proposalId = claim.ProposeDecision(As(maker), Approve("Repair cost verified")).Value;
        return (claim, proposalId);
    }

    public (Claim Claim, Guid ProposalId) PendingRejection()
    {
        var claim = UnderReview();
        var proposalId = claim.ProposeDecision(As(Officer), Reject("Damage predates the policy")).Value;
        return (claim, proposalId);
    }

    public static DecisionProposal Approve(string rationale, string? justification = null) =>
        new(ProposalOutcome.Approve, null, rationale, justification);

    public static DecisionProposal Reject(string rationale) =>
        new(ProposalOutcome.Reject, RejectReasonCode.NotCovered, rationale, null, "Your policy excludes flood damage.");
}
