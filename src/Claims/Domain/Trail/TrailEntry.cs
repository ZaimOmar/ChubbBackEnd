using Chubb.Contracts;

namespace Chubb.Claims.Domain;

public sealed record TrailEntry
{
    private static readonly HashSet<string> KnownRoles =
        [Roles.Claimant, Roles.Officer, Roles.Manager, Roles.System];

    // EF Core materialises stored entries through this constructor; new entries come only from the factories.
    private TrailEntry()
    {
        ActorRole = null!;
        Summary = null!;
    }

    private TrailEntry(CommandContext context, Guid claimId, TrailEntryDraft draft)
    {
        Id = context.Ids.NewId();
        ClaimId = claimId;
        OccurredAt = context.Now;
        EventType = draft.Type;
        FromStatus = draft.FromStatus;
        ToStatus = draft.ToStatus;
        ActorId = context.Actor.UserId;
        ActorRole = draft.ActorRole;
        Summary = draft.Summary;
        Reason = draft.Reason;
        Visibility = draft.Type.VisibilityOf();
        CorrelationId = context.CorrelationId;
        ProposalId = draft.ProposalId;
        MakerId = draft.MakerId;
        CheckerId = draft.CheckerId;
        ReferencedEntryId = draft.ReferencedEntryId;
        ReserveBefore = draft.ReserveBefore;
        ReserveAfter = draft.ReserveAfter;
    }

    public Guid Id { get; }

    public Guid ClaimId { get; }

    public DateTimeOffset OccurredAt { get; }

    public TrailEntryType EventType { get; }

    public ClaimStatus? FromStatus { get; }

    public ClaimStatus? ToStatus { get; }

    public Guid ActorId { get; }

    public string ActorRole { get; }

    public string Summary { get; }

    public string? Reason { get; }

    public TrailVisibility Visibility { get; }

    public Guid CorrelationId { get; }

    public Guid? ProposalId { get; }

    public Guid? MakerId { get; }

    public Guid? CheckerId { get; }

    public Guid? ReferencedEntryId { get; }

    public decimal? ReserveBefore { get; }

    public decimal? ReserveAfter { get; }

    public static TrailEntry Create(CommandContext context, Guid claimId, TrailEntryDraft draft)
    {
        EnsureMandatoryFields(context, claimId, draft);
        EnsureStatusPair(draft);
        EnsureReserveFields(draft);
        EnsurePublicEntryHasNoStaffData(draft);
        return new TrailEntry(context, claimId, draft);
    }

    public static TrailEntry Refused(Guid claimId, CommandContext context, DomainRefusal refusal)
    {
        if (!refusal.IsTrailed)
        {
            throw new ArgumentException("Only resource-rule refusals are written to the trail.", nameof(refusal));
        }

        var summary = TrailSummaries.Refused(refusal.Command, refusal.Type);
        return Create(context, claimId, new TrailEntryDraft(TrailEntryType.Refused, context.Actor.PrimaryRole, summary));
    }

    private static void EnsureMandatoryFields(CommandContext context, Guid claimId, TrailEntryDraft draft)
    {
        if (claimId == Guid.Empty || context.Actor.UserId == Guid.Empty || context.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("A trail entry needs a claim, an actor and a correlation id.");
        }

        if (!KnownRoles.Contains(draft.ActorRole))
        {
            throw new ArgumentException($"Unknown actor role '{draft.ActorRole}'.");
        }

        if (!context.Actor.Roles.Contains(draft.ActorRole))
        {
            throw new ArgumentException($"The actor does not hold the role '{draft.ActorRole}'.");
        }

        if (string.IsNullOrWhiteSpace(draft.Summary))
        {
            throw new ArgumentException("A trail entry needs a summary.");
        }
    }

    private static void EnsureStatusPair(TrailEntryDraft draft)
    {
        var isSubmission = draft.Type == TrailEntryType.Submitted;
        var pairIsConsistent = isSubmission
            ? draft.FromStatus is null && draft.ToStatus == ClaimStatus.Submitted
            : (draft.FromStatus is null) == (draft.ToStatus is null);
        if (!pairIsConsistent)
        {
            throw new ArgumentException("A status change needs both a from and a to status.");
        }
    }

    // ReserveBefore may be null only on the Submitted entry, where the reserve is created.
    private static void EnsureReserveFields(TrailEntryDraft draft)
    {
        var isSubmission = draft.Type == TrailEntryType.Submitted;
        var isValid = draft.ReserveBefore is null
            ? isSubmission == (draft.ReserveAfter is not null)
            : draft.ReserveAfter is not null;
        if (!isValid)
        {
            throw new ArgumentException("ReserveAfter is required whenever ReserveBefore is set, and ReserveBefore may be null only on Submitted.");
        }
    }

    private static void EnsurePublicEntryHasNoStaffData(TrailEntryDraft draft)
    {
        var hasStaffData = draft.MakerId is not null || draft.CheckerId is not null
            || draft.Reason is not null || draft.ReferencedEntryId is not null;
        if (draft.Type.VisibilityOf() == TrailVisibility.Public && hasStaffData)
        {
            throw new ArgumentException("A public trail entry cannot carry maker, checker, reason or reference.");
        }
    }
}
