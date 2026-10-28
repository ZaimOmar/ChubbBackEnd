using Chubb.Contracts;
using Chubb.Reporting.Api.Problems;
using Chubb.Reporting.Infrastructure.Queries;

namespace Chubb.Reporting.Api.Endpoints;

public static class WorkloadEndpoints
{
    public static IEndpointRouteBuilder MapWorkloadEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/staff/workload", GetWorkloadAsync).RequireAuthorization(ReportingPolicies.Staff);
        return routes;
    }

    // An officer sees only their own claims; a manager sees every officer.
    private static async Task<IResult> GetWorkloadAsync(
        WorkloadQueries queries, HttpContext context, CancellationToken cancellationToken)
    {
        if (context.User.IsInRole(Roles.Manager))
        {
            return Results.Ok(await queries.GetWorkloadAsync(null, cancellationToken));
        }

        return Guid.TryParse(context.User.FindFirst(ClaimNames.Subject)?.Value, out var officerId)
            ? Results.Ok(await queries.GetWorkloadAsync(officerId, cancellationToken))
            : ReportingProblems.Forbidden(context);
    }
}
