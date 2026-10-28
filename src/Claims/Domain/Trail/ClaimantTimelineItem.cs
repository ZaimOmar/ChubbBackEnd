namespace Chubb.Claims.Domain;

public sealed record ClaimantTimelineItem(Guid Id, DateTimeOffset OccurredAt, string Summary);
