using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

public sealed record StaffTimelineEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    TrailEntryType EventType,
    ClaimStatus? FromStatus,
    ClaimStatus? ToStatus,
    Guid ActorId,
    string ActorRole,
    string Summary,
    string? Reason,
    TrailVisibility Visibility,
    Guid? ProposalId,
    Guid? MakerId,
    Guid? CheckerId,
    Guid? ReferencedEntryId,
    decimal? ReserveBefore,
    decimal? ReserveAfter);
