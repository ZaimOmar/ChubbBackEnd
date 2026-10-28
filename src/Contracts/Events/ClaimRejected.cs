namespace Chubb.Contracts.Events;

public sealed record ClaimRejected(
    string Status,
    Guid ClaimantId,
    Guid ProposalId,
    Guid MakerId,
    Guid CheckerId,
    string ReasonCode,
    decimal ReserveReleased,
    string Currency,
    string ClaimNumber) : IClaimEventPayload
{
    public const string EventType = "ClaimRejected";
    public const int Version = 1;
}
