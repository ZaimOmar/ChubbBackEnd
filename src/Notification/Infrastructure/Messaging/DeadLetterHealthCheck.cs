using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chubb.Notification.Infrastructure.Messaging;

public sealed class DeadLetterHealthCheck(DeadLetterState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.IsRecent()
            ? HealthCheckResult.Degraded("A message was dead-lettered in the last 15 minutes.")
            : HealthCheckResult.Healthy());
}
