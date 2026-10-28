namespace Chubb.Claims.Api;

// Structured security events: ids, roles, route template and outcome only; never bodies, keys or tokens.
public static partial class SecurityEvents
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "RolePolicyDenied actor {ActorId} roles {Roles} route {RouteTemplate} claim {ClaimId} correlation {CorrelationId} outcome {Outcome}")]
    public static partial void RolePolicyDenied(
        ILogger logger, string actorId, string roles, string routeTemplate, string claimId, string correlationId, string outcome);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning,
        Message = "OwnershipDenied actor {ActorId} route {RouteTemplate} claim {ClaimId} correlation {CorrelationId} outcome {Outcome}")]
    public static partial void OwnershipDenied(
        ILogger logger, string actorId, string routeTemplate, string claimId, string correlationId, string outcome);
}
