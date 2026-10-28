namespace Chubb.Reporting.Infrastructure.Messaging;

public sealed class DeadLetterState(TimeProvider clock)
{
    public static readonly TimeSpan DegradedFor = TimeSpan.FromMinutes(15);

    private long _lastDeadLetterTicks;

    public void Record() => Interlocked.Exchange(ref _lastDeadLetterTicks, clock.GetUtcNow().UtcTicks);

    public bool IsRecent()
    {
        var ticks = Interlocked.Read(ref _lastDeadLetterTicks);
        return ticks != 0 && clock.GetUtcNow().UtcTicks - ticks < DegradedFor.Ticks;
    }
}
