namespace Chubb.Contracts.Events;

public sealed record ProposalWithdrawn(
    string Status, Guid ProposalId, Guid MakerId, string Cause, string ClaimNumber) : IClaimEventPayload
{
    public const string EventType = "ProposalWithdrawn";
    public const int Version = 1;

    public const string CauseByMaker = "ByMaker";
    public const string CauseOnReassignment = "OnReassignment";
}
