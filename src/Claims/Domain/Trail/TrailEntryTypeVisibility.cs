namespace Chubb.Claims.Domain;

public static class TrailEntryTypeVisibility
{
    public static TrailVisibility VisibilityOf(this TrailEntryType type) => type switch
    {
        TrailEntryType.Submitted or TrailEntryType.Assigned or TrailEntryType.DecisionUnderReview
            or TrailEntryType.DecisionAnnounced => TrailVisibility.Public,
        TrailEntryType.Assessed or TrailEntryType.DecisionProposed or TrailEntryType.DecisionConfirmed
            or TrailEntryType.DecisionSentBack or TrailEntryType.Refused => TrailVisibility.Internal,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown trail entry type."),
    };
}
