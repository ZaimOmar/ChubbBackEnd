namespace Chubb.Reporting.Domain;

public sealed class ClaimSnapshot
{
    private ClaimSnapshot()
    {
    }

    public Guid ClaimId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public string Market { get; private set; } = string.Empty;

    public string Type { get; private set; } = string.Empty;

    public string Currency { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public decimal Reserve { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public string? AssigneeName { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset LastEventAt { get; private set; }

    public static ClaimSnapshot Submitted(
        Guid claimId, string number, string market, string type, string currency, decimal reserve, DateTimeOffset at)
    {
        if (!FxRates.IsSupported(currency))
        {
            throw new InvalidEventException("The claim currency has no reporting rate.");
        }

        return new ClaimSnapshot
        {
            ClaimId = claimId,
            Number = number,
            Market = market,
            Type = type,
            Currency = currency,
            Status = ClaimStatuses.Submitted,
            Reserve = reserve,
            SubmittedAt = at,
            LastEventAt = at,
        };
    }

    public bool IsOlderThan(DateTimeOffset occurredAt) => occurredAt < LastEventAt;

    public void Assign(string status, Guid officerId, string? officerName, DateTimeOffset at)
    {
        AssigneeId = officerId;
        AssigneeName = officerName;
        MoveTo(status, at);
    }

    public void ChangeReserve(string status, decimal reserve, DateTimeOffset at)
    {
        Reserve = reserve;
        MoveTo(status, at);
    }

    public void MoveTo(string status, DateTimeOffset at)
    {
        Status = status;
        LastEventAt = at;
    }
}
