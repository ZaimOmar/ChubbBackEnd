using Chubb.Contracts;
using Chubb.Contracts.Events;

namespace Chubb.Claims.Domain;

public sealed partial class Claim
{
    public Result<Guid> ProposeDecision(CommandContext context, DecisionProposal request)
    {
        var refusal = RefuseUnlessOfficerIsWorking(ClaimCommand.ProposeDecision, context) ?? CheckProposal(request);
        if (refusal is not null)
        {
            return Result<Guid>.Failure(refusal);
        }

        var amount = request.Outcome == ProposalOutcome.Approve ? NetPayable : null;
        var proposal = new Proposal(context.Ids.NewId(), request, amount, context.Actor.UserId, context.Now);
        var isFirstProposal = _proposals.Count == 0;
        _proposals.Add(proposal);
        MoveTo(ClaimStatus.PendingDecisionApproval, context);
        RecordProposal(context, proposal, isFirstProposal);
        Raise(new DecisionProposed(
            Status.ToString(), proposal.Id, proposal.MakerId, proposal.Outcome.ToString(), amount?.Amount, Currency, Number.ToString()));
        return Result<Guid>.Success(proposal.Id);
    }

    public Result ConfirmDecision(CommandContext context, Guid proposalId)
    {
        var found = FindProposalForChecker(ClaimCommand.ConfirmDecision, context, proposalId);
        if (found.Refusal is { } refusal)
        {
            return Result.Failure(refusal);
        }

        var proposal = found.Value;
        var reserveBefore = Reserve;
        proposal.Confirm(context.Actor.UserId, context.Now);
        if (proposal.Outcome == ProposalOutcome.Approve)
        {
            ApplyApproval(context, ApprovedAmount(proposal));
        }
        else
        {
            ApplyRejection(context);
        }

        RecordConfirmation(context, proposal, reserveBefore);
        RaiseConfirmation(proposal, reserveBefore);
        return Result.Success;
    }

    public Result SendBackDecision(CommandContext context, Guid proposalId, string reason)
    {
        var found = FindProposalForChecker(ClaimCommand.SendBackDecision, context, proposalId);
        if (found.Refusal is { } refusal)
        {
            return Result.Failure(refusal);
        }

        if (!HasMinimumLength(reason))
        {
            return Result.Failure(DomainRefusals.ValidationError(ClaimCommand.SendBackDecision, "A reason of at least 10 characters is required."));
        }

        var proposal = found.Value;
        proposal.SendBack(context.Actor.UserId, reason, context.Now);
        MoveTo(ClaimStatus.UnderReview, context);
        RecordSendBack(context, proposal, reason);
        Raise(new DecisionSentBack(Status.ToString(), proposal.Id, proposal.MakerId, context.Actor.UserId, Number.ToString()));
        return Result.Success;
    }

    private void RecordSendBack(CommandContext context, Proposal proposal, string reason) =>
        Record(context, new TrailEntryDraft(TrailEntryType.DecisionSentBack, Roles.Manager, TrailSummaries.SentBack())
        {
            FromStatus = ClaimStatus.PendingDecisionApproval,
            ToStatus = ClaimStatus.UnderReview,
            Reason = reason,
            ProposalId = proposal.Id,
            MakerId = proposal.MakerId,
            CheckerId = context.Actor.UserId,
        });

    private static bool HasMinimumLength(string text) => text.Trim().Length >= MinReasonLength;

    private static Money ApprovedAmount(Proposal proposal) =>
        proposal.Amount ?? throw new InvalidOperationException("An approve proposal always has an amount.");

    private static string ClaimantReason(Proposal proposal) =>
        proposal.ClaimantReason ?? throw new InvalidOperationException("A reject proposal always has a claimant reason.");

    private static RejectReasonCode RejectionCode(Proposal proposal) =>
        proposal.ReasonCode ?? throw new InvalidOperationException("A reject proposal always has a reason code.");

    private DomainRefusal? CheckProposal(DecisionProposal request)
    {
        if (!HasMinimumLength(request.Rationale))
        {
            return DomainRefusals.ValidationError(ClaimCommand.ProposeDecision, "A rationale of at least 10 characters is required.");
        }

        return request.Outcome switch
        {
            ProposalOutcome.Reject => CheckRejectCode(request) ?? CheckClaimantReason(request),
            ProposalOutcome.Approve => CheckApprovable(request),
            _ => DomainRefusals.ValidationError(ClaimCommand.ProposeDecision, "Unknown outcome."),
        };
    }

    private static DomainRefusal? CheckClaimantReason(DecisionProposal request)
    {
        var length = request.ClaimantReason?.Trim().Length ?? 0;
        return length is >= MinReasonLength and <= MaxClaimantReasonLength
            ? null
            : DomainRefusals.ValidationError(ClaimCommand.ProposeDecision, "A claimant reason of 10 to 500 characters is required.");
    }

    private static DomainRefusal? CheckRejectCode(DecisionProposal request) =>
        request.ReasonCode is { } code && Enum.IsDefined(code) ? null : DomainRefusals.ReasonCodeInvalid(ClaimCommand.ProposeDecision);

    private DomainRefusal? CheckApprovable(DecisionProposal request)
    {
        if (request.ClaimantReason is not null || request.ReasonCode is not null)
        {
            return DomainRefusals.ValidationError(ClaimCommand.ProposeDecision, "An approve proposal has no reason code or claimant reason.");
        }

        if (NetPayable is not { } payable || Assessed is null)
        {
            return DomainRefusals.AssessedAmountRequired(ClaimCommand.ProposeDecision);
        }

        if (!payable.IsPositive)
        {
            return DomainRefusals.PayableZero(ClaimCommand.ProposeDecision);
        }

        var needsJustification = payable.IsGreaterThan(Estimated) && string.IsNullOrWhiteSpace(request.Justification);
        return needsJustification ? DomainRefusals.JustificationRequired(ClaimCommand.ProposeDecision) : null;
    }

    // The proposal is checked before the maker so a stale id is "not open" even for the maker.
    private Result<Proposal> FindProposalForChecker(ClaimCommand command, CommandContext context, Guid proposalId)
    {
        if (!context.Actor.IsManager)
        {
            return Result<Proposal>.Failure(DomainRefusals.Forbidden(command));
        }

        var proposal = Status == ClaimStatus.PendingDecisionApproval
            ? _proposals.FirstOrDefault(candidate => candidate.Id == proposalId && candidate.IsOpen)
            : null;
        if (proposal is null)
        {
            return Result<Proposal>.Failure(DomainRefusals.ProposalNotOpen(command, proposalId));
        }

        return proposal.MakerId == context.Actor.UserId
            ? Result<Proposal>.Failure(DomainRefusals.MakerCannotCheck(command))
            : Result<Proposal>.Success(proposal);
    }

    private void ApplyApproval(CommandContext context, Money payable)
    {
        _payable = payable.Amount;
        _reserve = payable.Amount;
        MoveTo(ClaimStatus.Approved, context);
    }

    private void ApplyRejection(CommandContext context)
    {
        _reserve = 0m;
        MoveTo(ClaimStatus.Rejected, context);
    }

    private void RecordProposal(CommandContext context, Proposal proposal, bool isFirstProposal)
    {
        Record(context, new TrailEntryDraft(
            TrailEntryType.DecisionProposed, Roles.Officer, TrailSummaries.Proposed(proposal.Outcome, proposal.Amount))
        {
            FromStatus = ClaimStatus.UnderReview,
            ToStatus = ClaimStatus.PendingDecisionApproval,
            Reason = ProposalReason(proposal),
            ProposalId = proposal.Id,
            MakerId = proposal.MakerId,
        });
        if (isFirstProposal)
        {
            Record(context, new TrailEntryDraft(
                TrailEntryType.DecisionUnderReview, Roles.Officer, TrailSummaries.DecisionUnderReview())
            {
                ProposalId = proposal.Id,
            });
        }
    }

    // The BR-18 justification is staff-only, so it travels in the Internal entry's reason.
    private static string ProposalReason(Proposal proposal) => string.IsNullOrWhiteSpace(proposal.Justification)
        ? proposal.Rationale
        : $"{proposal.Rationale} Justification: {proposal.Justification}";

    private void RecordConfirmation(CommandContext context, Proposal proposal, Money reserveBefore)
    {
        Record(context, new TrailEntryDraft(
            TrailEntryType.DecisionConfirmed, Roles.Manager, TrailSummaries.Confirmed(proposal.Outcome, proposal.Amount))
        {
            FromStatus = ClaimStatus.PendingDecisionApproval,
            ToStatus = Status,
            Reason = proposal.Rationale,
            ProposalId = proposal.Id,
            MakerId = proposal.MakerId,
            CheckerId = context.Actor.UserId,
            ReserveBefore = reserveBefore.Amount,
            ReserveAfter = _reserve,
        });
        var announcement = proposal.Outcome == ProposalOutcome.Approve
            ? TrailSummaries.Approved(ApprovedAmount(proposal))
            : TrailSummaries.Rejected(RejectionCode(proposal), ClaimantReason(proposal));
        Record(context, new TrailEntryDraft(TrailEntryType.DecisionAnnounced, Roles.Manager, announcement)
        {
            ProposalId = proposal.Id,
        });
    }

    private void RaiseConfirmation(Proposal proposal, Money reserveBefore)
    {
        var checkerId = proposal.CheckerId ?? throw new InvalidOperationException("A confirmed proposal has a checker.");
        IClaimEventPayload payload = proposal.Outcome == ProposalOutcome.Approve
            ? new ClaimApproved(Status.ToString(), ClaimantId, proposal.Id, proposal.MakerId, checkerId, _reserve, Currency, Number.ToString())
            : new ClaimRejected(
                Status.ToString(), ClaimantId, proposal.Id, proposal.MakerId, checkerId,
                RejectionCode(proposal).WireCodeOf(), reserveBefore.Amount, Currency, Number.ToString());
        Raise(payload);
    }
}
