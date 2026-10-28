namespace Chubb.Contracts.Events;

public sealed record DecisionSentBack(string Status, Guid ProposalId, Guid MakerId, Guid CheckerId, string ClaimNumber) : IClaimEventPayload
{
    public const string EventType = "DecisionSentBack";
    public const int Version = 1;
}
