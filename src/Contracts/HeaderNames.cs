namespace Chubb.Contracts;

public static class HeaderNames
{
    public const string CorrelationId = "X-Correlation-Id";
    public const string IdempotencyKey = "Idempotency-Key";
    public const string IdempotentReplayed = "Idempotent-Replayed";
}
