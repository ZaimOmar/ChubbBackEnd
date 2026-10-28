namespace Chubb.Contracts.Events;

public sealed record DecisionProposed(
    string Status,
    Guid ProposalId,
    Guid MakerId,
    string Outcome,
    decimal? Amount,
    string Currency,
    string ClaimNumber) : IClaimEventPayload
{
    public const string EventType = "DecisionProposed";
    public const int Version = 1;
}
