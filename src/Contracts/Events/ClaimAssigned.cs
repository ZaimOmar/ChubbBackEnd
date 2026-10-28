namespace Chubb.Contracts.Events;

public sealed record ClaimAssigned(
    string Status, Guid ClaimantId, Guid OfficerId, string ClaimNumber, string? AssigneeName) : IClaimEventPayload
{
    public const string EventType = "ClaimAssigned";
    public const int Version = 1;
}
