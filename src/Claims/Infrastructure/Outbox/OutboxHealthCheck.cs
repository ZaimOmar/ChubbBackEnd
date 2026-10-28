using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chubb.Claims.Infrastructure.Outbox;

/// <summary>Degraded, not Unhealthy: a stuck outbox row delays events but the command API still works.</summary>
public sealed class OutboxHealthCheck(OutboxPublisherState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var stuck = state.Stuck;
        var result = stuck is null
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded(
                $"Outbox publisher is stuck on event {stuck.EventId} (sequence {stuck.Sequence}, {stuck.Attempts} attempts).");
        return Task.FromResult(result);
    }
}
