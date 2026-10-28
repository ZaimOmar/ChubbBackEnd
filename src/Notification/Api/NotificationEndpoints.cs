using System.Security.Claims;
using Chubb.Contracts;
using Chubb.Notification.Application;

namespace Chubb.Notification.Api;

public static class NotificationEndpoints
{
    public const int DefaultPageSize = 20;

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/notifications").RequireAuthorization(AuthenticationSetup.InboxPolicy);
        group.MapGet("/", ListAsync);
        group.MapPost("/{id:guid}/read", MarkReadAsync);
        return routes;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context, InboxService inbox, CancellationToken cancellationToken, int page = 1, int pageSize = DefaultPageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > InboxService.MaxPageSize)
        {
            return Problems.InvalidPaging(context, InboxService.MaxPageSize);
        }

        var scope = ScopeOf(context.User);
        return scope is null
            ? Problems.Unauthorized(context)
            : Results.Ok(await inbox.ListAsync(scope, page, pageSize, cancellationToken));
    }

    // Marking read twice is harmless, so no Idempotency-Key is needed.
    private static async Task<IResult> MarkReadAsync(HttpContext context, Guid id, InboxService inbox, CancellationToken cancellationToken)
    {
        var scope = ScopeOf(context.User);
        if (scope is null)
        {
            return Problems.Unauthorized(context);
        }

        return await inbox.MarkReadAsync(scope, id, cancellationToken)
            ? Results.NoContent()
            : Problems.NotFound(context);
    }

    private static InboxScope? ScopeOf(ClaimsPrincipal user)
    {
        if (user.IsInRole(Roles.Manager))
        {
            return InboxScope.ForManagers();
        }

        return Guid.TryParse(user.FindFirstValue(ClaimNames.Subject), out var userId) ? InboxScope.ForClaimant(userId) : null;
    }
}
