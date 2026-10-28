namespace Chubb.Claims.Api;

public sealed record AssessedAmountRequest
{
    public required decimal AssessedAmount { get; init; }

    public string Note { get; init; } = string.Empty;
}
