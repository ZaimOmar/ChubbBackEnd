namespace Chubb.Contracts.Events;

public sealed record ClaimApproved(
    string Status,
    Guid ClaimantId,
    Guid ProposalId,
    Guid MakerId,
    Guid CheckerId,
    decimal Payable,
    string Currency,
    string ClaimNumber) : IClaimEventPayload
{
    public const string EventType = "ClaimApproved";
    public const int Version = 1;
}
