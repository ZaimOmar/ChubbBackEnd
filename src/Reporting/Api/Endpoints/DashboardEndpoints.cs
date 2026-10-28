using Chubb.Reporting.Api.Problems;
using Chubb.Reporting.Application;
using Chubb.Reporting.Infrastructure.Queries;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Chubb.Reporting.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        var dashboard = routes.MapGroup("/dashboard").RequireAuthorization(ReportingPolicies.Manager);
        dashboard.MapGet("/outstanding", GetOutstandingAsync);
        dashboard.MapGet("/exposure", GetExposureAsync);
        dashboard.MapGet("/pending-approvals", GetPendingApprovalsAsync);
        return routes;
    }

    private static async Task<IResult> GetOutstandingAsync(DashboardQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.GetOutstandingAsync(cancellationToken));

    private static async Task<IResult> GetExposureAsync(DashboardQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.GetExposureAsync(cancellationToken));

    private static async Task<IResult> GetPendingApprovalsAsync(
        [AsParameters] PageRequest request,
        IValidator<PageRequest> validator,
        DashboardQueries queries,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ReportingProblems.Validation(context, validation.ToDictionary());
        }

        return Results.Ok(await queries.GetPendingApprovalsAsync(request, cancellationToken));
    }
}
