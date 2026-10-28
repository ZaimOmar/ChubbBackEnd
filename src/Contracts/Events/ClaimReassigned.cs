namespace Chubb.Contracts.Events;

public sealed record ClaimReassigned(
    string Status, Guid PreviousOfficerId, Guid NewOfficerId, string ClaimNumber) : IClaimEventPayload
{
    public const string EventType = "ClaimReassigned";
    public const int Version = 1;
}
