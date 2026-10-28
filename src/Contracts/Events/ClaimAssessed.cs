namespace Chubb.Contracts.Events;

public sealed record ClaimAssessed(
    string Status,
    decimal AssessedAmount,
    decimal Excess,
    decimal NetPayable,
    decimal ReserveBefore,
    decimal ReserveAfter,
    string Currency) : IClaimEventPayload
{
    public const string EventType = "ClaimAssessed";
    public const int Version = 1;
}
