namespace Chubb.Claims.Infrastructure.Outbox;

public sealed class OutboxPublisherOptions
{
    public const string SectionName = "Outbox";

    public const string EnabledKey = "Outbox:Enabled";

    public int BatchSize { get; init; } = 100;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public int MaxAttempts { get; init; } = 10;

    public TimeSpan BackoffInitial { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan BackoffCap { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Time allowed to mark and commit rows that were already acknowledged when the host is stopping.</summary>
    public TimeSpan ShutdownGrace { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan BackoffFor(int attempts)
    {
        var doublings = Math.Clamp(attempts - 1, 0, 30);
        var delay = TimeSpan.FromTicks(Math.Min(BackoffInitial.Ticks * (1L << doublings), BackoffCap.Ticks));
        return delay < BackoffInitial ? BackoffInitial : delay;
    }

    public bool IsValid() =>
        BatchSize > 0 && MaxAttempts > 0 && PollInterval > TimeSpan.Zero && ShutdownGrace > TimeSpan.Zero
        && BackoffInitial > TimeSpan.Zero && BackoffCap >= BackoffInitial;
}
