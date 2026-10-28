namespace Chubb.Claims.Domain;

public static class ClaimantTimeline
{
    public static IReadOnlyList<ClaimantTimelineItem> Project(IEnumerable<TrailEntry> entries) => entries
        .Where(entry => entry.Visibility == TrailVisibility.Public)
        .OrderBy(entry => entry.OccurredAt)
        .ThenBy(entry => entry.Id)
        .Select(entry => new ClaimantTimelineItem(entry.Id, entry.OccurredAt, entry.Summary))
        .ToList();
}
