using Chubb.Claims.Api.Problems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Chubb.Claims.Api;

// A role-policy 403 only logs a security event: no trail entry and no idempotency record (ADR-009).
public sealed class ForbiddenResultHandler(ILogger<ForbiddenResultHandler> logger) : IAuthorizationMiddlewareResultHandler
{
    private const string DeniedOutcome = "forbidden";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var correlationId = CorrelationIdMiddleware.From(context);
        if (authorizeResult.Challenged)
        {
            await ProblemResults.Unauthorized(correlationId).ToResult().ExecuteAsync(context);
            return;
        }

        if (!authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        LogDenied(context, correlationId);
        await ProblemResults.Forbidden(correlationId).ToResult().ExecuteAsync(context);
    }

    private void LogDenied(HttpContext context, string correlationId)
    {
        var actorId = context.User.FindFirst(Contracts.ClaimNames.Subject)?.Value ?? "unknown";
        var roles = string.Join(',', context.User.FindAll(Contracts.ClaimNames.Role).Select(claim => claim.Value));
        var template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown";
        var claimId = context.GetRouteValue("id")?.ToString() ?? "none";
        SecurityEvents.RolePolicyDenied(logger, actorId, roles, template, claimId, correlationId, DeniedOutcome);
    }
}
