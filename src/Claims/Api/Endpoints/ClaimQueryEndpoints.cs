using Chubb.Claims.Api.Problems;
using Chubb.Claims.Application;
using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

public static class ClaimQueryEndpoints
{
    public static IEndpointRouteBuilder MapClaimQueryEndpoints(this IEndpointRouteBuilder routes)
    {
        var claims = routes.MapGroup("/claims").WithTags("Claims");
        claims.MapGet("", ListAsync).RequireAuthorization(ClaimsPolicies.Staff).WithValidation<ClaimListQuery>();
        claims.MapGet("/mine", ListMineAsync).RequireAuthorization(ClaimsPolicies.Claimant).WithValidation<MyClaimsQuery>();
        claims.MapGet("/{id:guid}", GetAsync).RequireAuthorization(ClaimsPolicies.AnyUser);
        claims.MapGet("/{id:guid}/timeline", GetTimelineAsync).RequireAuthorization(ClaimsPolicies.AnyUser);
        return routes;
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] ClaimListQuery query, IClaimQueries queries, CancellationToken ct)
    {
        var filter = new ClaimListFilter(query.Market, query.Type, query.Status, query.AssigneeId, ColumnsOf(query), query.Outstanding, query.Page, query.PageSize, SortOf(query));
        var page = await queries.ListAsync(filter, ct);
        return Results.Ok(new StaffClaimPage(page.Items.Select(ClaimViews.ToStaff).ToList(), query.Page, query.PageSize, page.TotalCount));
    }

    private static async Task<IResult> ListMineAsync(
        [AsParameters] MyClaimsQuery query, HttpContext http, IClaimQueries queries, CancellationToken ct)
    {
        var actor = ActorFactory.TryCreate(http.User) ?? throw new UnauthorizedAccessException();
        var sort = ClaimListSortParser.TryParse(query.Sort ?? "", out var parsed) ? parsed : ClaimListSort.Newest;
        var page = await queries.ListMineAsync(new MyClaimsFilter(actor.UserId, query.Market, query.Type, query.Status, ColumnsOf(query), query.Page, query.PageSize, sort), ct);
        return Results.Ok(new ClaimantClaimPage(page.Items.Select(ClaimViews.ToClaimant).ToList(), query.Page, query.PageSize, page.TotalCount));
    }

    private static ClaimColumnCriteria ColumnsOf(ClaimListQuery query) =>
        new(TrimmedOrNull(query.ClaimNumber), query.MinAmount, query.MaxAmount, query.SubmittedFrom, query.SubmittedTo);

    private static ClaimColumnCriteria ColumnsOf(MyClaimsQuery query) =>
        new(TrimmedOrNull(query.ClaimNumber), query.MinAmount, query.MaxAmount, query.SubmittedFrom, query.SubmittedTo);

    private static string? TrimmedOrNull(string? text) => text?.Trim();

    // The checker queue defaults to the longest-waiting proposal first.
    private static ClaimListSort SortOf(ClaimListQuery query)
    {
        if (query.Sort is null)
        {
            return query.Status == ClaimStatus.PendingDecisionApproval ? ClaimListSort.Oldest : ClaimListSort.Newest;
        }

        ClaimListSortParser.TryParse(query.Sort, out var sort);
        return sort;
    }

    private static async Task<IResult> GetAsync(
        Guid id, HttpContext http, IClaimQueries queries, ILogger<IClaimQueries> logger, CancellationToken ct)
    {
        var actor = ActorFactory.TryCreate(http.User) ?? throw new UnauthorizedAccessException();
        var claim = await queries.FindAsync(id, actor, ct);
        if (claim is null)
        {
            return NotFound(http, logger, actor, id);
        }

        return actor.IsStaff ? Results.Ok(ClaimViews.ToStaff(claim)) : Results.Ok(ClaimViews.ToClaimant(claim));
    }

    private static async Task<IResult> GetTimelineAsync(
        Guid id, HttpContext http, IClaimQueries queries, ILogger<IClaimQueries> logger, CancellationToken ct)
    {
        var actor = ActorFactory.TryCreate(http.User) ?? throw new UnauthorizedAccessException();
        var trail = await queries.GetTrailAsync(id, actor, ct);
        if (trail is null)
        {
            return NotFound(http, logger, actor, id);
        }

        return actor.IsStaff
            ? Results.Ok(trail.Select(ClaimViews.ToStaffEntry).ToList())
            : Results.Ok(ClaimantTimeline.Project(trail));
    }

    // Missing and not-owned claims answer identically; only the log tells them apart for operators.
    private static IResult NotFound(HttpContext http, ILogger logger, Actor actor, Guid claimId)
    {
        var correlationId = CorrelationIdMiddleware.From(http);
        var template = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown";
        SecurityEvents.OwnershipDenied(logger, actor.UserId.ToString(), template, claimId.ToString(), correlationId, "not-found");
        return ProblemResults.ClaimNotFound(claimId, correlationId).ToResult();
    }
}
