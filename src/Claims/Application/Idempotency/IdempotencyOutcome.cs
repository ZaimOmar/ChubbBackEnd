namespace Chubb.Claims.Application;

public abstract record IdempotencyOutcome
{
    public sealed record Executed(StoredResponse Response) : IdempotencyOutcome;

    public sealed record Replayed(StoredResponse Response) : IdempotencyOutcome;

    public sealed record NotStored(StoredResponse Response) : IdempotencyOutcome;

    public sealed record InProgress : IdempotencyOutcome;

    public sealed record KeyReused : IdempotencyOutcome;
}
