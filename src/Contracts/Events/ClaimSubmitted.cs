namespace Chubb.Contracts.Events;

public sealed record ClaimSubmitted(
    string Status,
    Guid ClaimantId,
    string ClaimNumber,
    string ClaimType,
    string Currency,
    decimal EstimatedAmount,
    decimal Reserve) : IClaimEventPayload
{
    public const string EventType = "ClaimSubmitted";
    public const int Version = 1;
}
