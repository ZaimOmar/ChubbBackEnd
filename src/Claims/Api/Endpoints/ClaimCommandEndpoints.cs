using Chubb.Claims.Application;

namespace Chubb.Claims.Api;

public static class ClaimCommandEndpoints
{
    public static IEndpointRouteBuilder MapClaimCommandEndpoints(this IEndpointRouteBuilder routes)
    {
        var claims = routes.MapGroup("/claims").WithTags("Claims");
        claims.MapPost("", SubmitAsync)
            .RequireAuthorization(ClaimsPolicies.Claimant).WithIdempotency().WithValidation<SubmitClaimRequest>();
        claims.MapPost("/{id:guid}/assign", AssignAsync)
            .RequireAuthorization(ClaimsPolicies.Officer).WithIdempotency();
        claims.MapPost("/{id:guid}/assessed-amount", AssessAsync)
            .RequireAuthorization(ClaimsPolicies.Officer).WithIdempotency().WithValidation<AssessedAmountRequest>();
        claims.MapPost("/{id:guid}/decision-proposals", ProposeAsync)
            .RequireAuthorization(ClaimsPolicies.Officer).WithIdempotency().WithValidation<ProposeDecisionRequest>();
        claims.MapPost("/{id:guid}/decision-proposals/{pid:guid}/confirm", ConfirmAsync)
            .RequireAuthorization(ClaimsPolicies.Manager).WithIdempotency();
        claims.MapPost("/{id:guid}/decision-proposals/{pid:guid}/send-back", SendBackAsync)
            .RequireAuthorization(ClaimsPolicies.Manager).WithIdempotency().WithValidation<SendBackRequest>();
        return routes;
    }

    private static async Task<IResult> SubmitAsync(
        SubmitClaimRequest request, HttpContext http, CommandContextFactory contexts, SubmitClaimHandler handler, CancellationToken ct)
    {
        var claim = await handler.HandleAsync(request.ToCommand(), contexts.Create(http), ct);
        return Results.Created($"/claims/{claim.Id}", ClaimViews.ToClaimant(claim));
    }

    private static async Task<IResult> AssignAsync(
        Guid id, EmptyRequest request, HttpContext http, CommandContextFactory contexts, PickUpClaimHandler handler, CancellationToken ct)
    {
        var claim = await handler.HandleAsync(new PickUpClaimCommand(id), contexts.Create(http), ct);
        return Results.Ok(ClaimViews.ToStaff(claim));
    }

    private static async Task<IResult> AssessAsync(
        Guid id, AssessedAmountRequest request, HttpContext http, CommandContextFactory contexts, AssessClaimHandler handler, CancellationToken ct)
    {
        var command = new AssessClaimCommand(id, request.AssessedAmount, request.Note);
        return Results.Ok(ClaimViews.ToStaff(await handler.HandleAsync(command, contexts.Create(http), ct)));
    }

    private static async Task<IResult> ProposeAsync(
        Guid id, ProposeDecisionRequest request, HttpContext http, CommandContextFactory contexts, ProposeDecisionHandler handler, CancellationToken ct)
    {
        var command = new ProposeDecisionCommand(id, request.ToDomain());
        return Results.Ok(ClaimViews.ToStaff(await handler.HandleAsync(command, contexts.Create(http), ct)));
    }

    private static async Task<IResult> ConfirmAsync(
        Guid id, Guid pid, HttpContext http, CommandContextFactory contexts, ConfirmDecisionHandler handler, CancellationToken ct) =>
        Results.Ok(ClaimViews.ToStaff(await handler.HandleAsync(new ConfirmDecisionCommand(id, pid), contexts.Create(http), ct)));

    private static async Task<IResult> SendBackAsync(
        Guid id, Guid pid, SendBackRequest request, HttpContext http, CommandContextFactory contexts, SendBackDecisionHandler handler, CancellationToken ct)
    {
        var command = new SendBackDecisionCommand(id, pid, request.Reason);
        return Results.Ok(ClaimViews.ToStaff(await handler.HandleAsync(command, contexts.Create(http), ct)));
    }
}
