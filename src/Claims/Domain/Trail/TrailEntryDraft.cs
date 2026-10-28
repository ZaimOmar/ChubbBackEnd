namespace Chubb.Claims.Domain;

public sealed record TrailEntryDraft(TrailEntryType Type, string ActorRole, string Summary)
{
    public ClaimStatus? FromStatus { get; init; }

    public ClaimStatus? ToStatus { get; init; }

    public string? Reason { get; init; }

    public Guid? ProposalId { get; init; }

    public Guid? MakerId { get; init; }

    public Guid? CheckerId { get; init; }

    public Guid? ReferencedEntryId { get; init; }

    public decimal? ReserveBefore { get; init; }

    public decimal? ReserveAfter { get; init; }
}
