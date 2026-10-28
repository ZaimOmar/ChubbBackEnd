using Chubb.Contracts;
using Chubb.Reporting.Api.Problems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Chubb.Reporting.Api;

public sealed class ProblemAuthorizationResultHandler(ILogger<ProblemAuthorizationResultHandler> logger) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await ReportingProblems.Unauthorized(context).ExecuteAsync(context);
            return;
        }

        if (!authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown";
        var actorId = context.User.FindFirst(ClaimNames.Subject)?.Value ?? "unknown";
        logger.LogWarning("Role policy denied actor {ActorId} on {Route}", actorId, route);
        await ReportingProblems.Forbidden(context).ExecuteAsync(context);
    }
}
