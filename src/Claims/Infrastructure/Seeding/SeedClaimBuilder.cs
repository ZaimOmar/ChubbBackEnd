using Chubb.Claims.Application;
using Chubb.Claims.Domain;
using Chubb.Contracts;

namespace Chubb.Claims.Infrastructure.Seeding;

// Drives the real handlers, so seeded trail entries, reserves and outbox rows equal what production writes.
internal sealed class SeedClaimBuilder(
    SubmitClaimHandler submit,
    PickUpClaimHandler pickUp,
    AssessClaimHandler assess,
    ProposeDecisionHandler propose,
    SendBackDecisionHandler sendBack,
    ConfirmDecisionHandler confirm,
    IIdGenerator ids)
{
    private const decimal AssessedShare = 0.9m;
    private const int OfficerRotation = 2;

    private static readonly Actor Officer = ActorOf(DevSeed.SeedUserIds.Officer, Roles.Officer);
    private static readonly Actor SecondOfficer = ActorOf(DevSeed.SeedUserIds.SecondOfficer, Roles.Officer);
    private static readonly Actor Manager = ActorOf(DevSeed.SeedUserIds.Manager, Roles.Manager);
    private static readonly Actor OfficerManager = ActorOf(DevSeed.SeedUserIds.OfficerManager, Roles.Officer, Roles.Manager);

    public async Task BuildAsync(SeedScenario scenario, int index, DateTimeOffset start, CancellationToken cancellationToken)
    {
        var correlationId = ids.NewId();
        var now = start;
        CommandContext Next(Actor actor)
        {
            now = now.AddHours(1);
            return new CommandContext(actor, now, correlationId, ids);
        }

        var claimant = ActorOf(SeedData.ClaimantId(scenario.ClaimantIndex), Roles.Claimant);
        var claim = await submit.HandleAsync(SubmitCommandFor(scenario, index, start), Next(claimant), cancellationToken);
        if (scenario.Stage == SeedStage.Submitted)
        {
            return;
        }

        var officer = OfficerFor(scenario.Stage, index);
        await pickUp.HandleAsync(new PickUpClaimCommand(claim.Id), Next(officer), cancellationToken);
        if (scenario.Stage == SeedStage.PickedUp)
        {
            return;
        }

        var assessed = Math.Round(scenario.Estimate * AssessedShare, 2);
        await assess.HandleAsync(new AssessClaimCommand(claim.Id, assessed, "Inspection completed"), Next(officer), cancellationToken);
        if (scenario.Stage != SeedStage.Assessed)
        {
            await DecideAsync(scenario.Stage, claim.Id, officer, Next, cancellationToken);
        }
    }

    private async Task DecideAsync(
        SeedStage stage, Guid claimId, Actor officer, Func<Actor, CommandContext> next, CancellationToken cancellationToken)
    {
        var proposal = stage == SeedStage.Rejected ? RejectProposal() : ApproveProposal();
        var claim = await propose.HandleAsync(new ProposeDecisionCommand(claimId, proposal), next(officer), cancellationToken);
        if (stage == SeedStage.ProposedAfterSendBack)
        {
            var sentBack = new SendBackDecisionCommand(claimId, claim.Proposals[^1].Id, "Please attach the repair invoice.");
            await sendBack.HandleAsync(sentBack, next(Manager), cancellationToken);
            claim = await propose.HandleAsync(new ProposeDecisionCommand(claimId, proposal), next(officer), cancellationToken);
        }

        if (stage is SeedStage.Approved or SeedStage.Rejected)
        {
            await confirm.HandleAsync(new ConfirmDecisionCommand(claimId, claim.Proposals[^1].Id), next(Manager), cancellationToken);
        }
    }

    private static SubmitClaimCommand SubmitCommandFor(SeedScenario scenario, int index, DateTimeOffset start)
    {
        ClaimDetails details = scenario.IsMotor
            ? new MotorDetails($"{scenario.Market}{1000 + index}A", "Main Road junction")
            : new PropertyDetails($"{index + 1} Harbour View", "Water damage");
        return new SubmitClaimCommand(
            SeedData.PolicyNumber(scenario.ClaimantIndex, scenario.Market),
            scenario.Market,
            DateOnly.FromDateTime(start.UtcDateTime.AddDays(-1)),
            "Seeded demo claim",
            scenario.Estimate,
            details);
    }

    private static Actor OfficerFor(SeedStage stage, int index) => stage == SeedStage.ProposedByOfficerManager
        ? OfficerManager
        : index % OfficerRotation == 0 ? Officer : SecondOfficer;

    private static DecisionProposal ApproveProposal() =>
        new(ProposalOutcome.Approve, null, "Repair cost verified against the invoice", null);

    private static DecisionProposal RejectProposal() => new(
        ProposalOutcome.Reject,
        RejectReasonCode.ExclusionApplies,
        "Policy wording excludes this loss",
        null,
        "Your policy does not cover this type of loss.");

    private static Actor ActorOf(Guid userId, params string[] roles) => new(userId, roles.ToHashSet());
}
