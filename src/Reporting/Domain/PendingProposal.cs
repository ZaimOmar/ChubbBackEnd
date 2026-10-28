namespace Chubb.Reporting.Domain;

public sealed class PendingProposal
{
    private PendingProposal()
    {
    }

    public Guid ProposalId { get; private set; }

    public Guid ClaimId { get; private set; }

    public Guid MakerId { get; private set; }

    public decimal? Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public DateTimeOffset ProposedAt { get; private set; }

    public static PendingProposal Create(
        Guid proposalId, Guid claimId, Guid makerId, decimal? amount, string currency, DateTimeOffset proposedAt) =>
        new()
        {
            ProposalId = proposalId,
            ClaimId = claimId,
            MakerId = makerId,
            Amount = amount,
            Currency = currency,
            ProposedAt = proposedAt,
        };
}
