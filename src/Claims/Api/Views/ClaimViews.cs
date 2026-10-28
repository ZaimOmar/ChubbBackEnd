using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

public static class ClaimViews
{
    public static ClaimantClaimView ToClaimant(Claim claim)
    {
        var status = ClaimantView.StatusFor(claim.Status);
        return new ClaimantClaimView(
            claim.Id,
            claim.Number.ToString(),
            claim.Type,
            claim.Market,
            claim.Currency,
            status,
            claim.IncidentDate,
            claim.Description,
            claim.Estimated.Amount,
            status == ClaimStatus.Approved ? claim.Payable?.Amount : null,
            status == ClaimStatus.Rejected ? RejectionOf(claim) : null,
            claim.SubmittedAt,
            claim.UpdatedAt);
    }

    private static RejectionReasonView? RejectionOf(Claim claim)
    {
        var confirmed = claim.Proposals.LastOrDefault(proposal =>
            proposal.State == ProposalState.Confirmed && proposal.Outcome == ProposalOutcome.Reject);
        return confirmed is { ReasonCode: { } code, ClaimantReason: { } reason }
            ? new RejectionReasonView(code.WireCodeOf(), code.LabelOf(), reason)
            : null;
    }

    public static StaffClaimView ToStaff(Claim claim) => new(
        claim.Id,
        claim.Number.ToString(),
        claim.Type,
        claim.Market,
        claim.Currency,
        claim.Status,
        claim.IncidentDate,
        claim.Description,
        claim.Estimated.Amount,
        claim.ClaimantId,
        claim.Policy.Id,
        claim.AssignedOfficerId,
        claim.Assessed?.Amount,
        claim.NetPayable?.Amount,
        claim.Reserve.Amount,
        claim.Payable?.Amount,
        claim.Proposals.Where(proposal => proposal.IsOpen).Select(ToOpenProposal).SingleOrDefault(),
        claim.SubmittedAt,
        claim.UpdatedAt);

    public static StaffTimelineEntry ToStaffEntry(TrailEntry entry) => new(
        entry.Id,
        entry.OccurredAt,
        entry.EventType,
        entry.FromStatus,
        entry.ToStatus,
        entry.ActorId,
        entry.ActorRole,
        entry.Summary,
        entry.Reason,
        entry.Visibility,
        entry.ProposalId,
        entry.MakerId,
        entry.CheckerId,
        entry.ReferencedEntryId,
        entry.ReserveBefore,
        entry.ReserveAfter);

    private static OpenProposalView ToOpenProposal(Proposal proposal) => new(
        proposal.Id,
        proposal.Outcome,
        proposal.Amount?.Amount,
        proposal.Currency,
        proposal.ReasonCode?.WireCodeOf(),
        proposal.Rationale,
        proposal.Justification,
        proposal.ClaimantReason,
        proposal.MakerId,
        proposal.ProposedAt);
}
