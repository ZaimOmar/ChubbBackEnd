namespace Chubb.Notification.Api;

// The single place where errors become RFC 7807 documents; details are fixed texts, never submitted values.
public static class Problems
{
    public static IResult NotFound(HttpContext context) =>
        Create(context, "notification-not-found", StatusCodes.Status404NotFound, "The notification was not found.");

    public static IResult InvalidPaging(HttpContext context, int maxPageSize) =>
        Create(context, "validation-error", StatusCodes.Status400BadRequest, $"page must be at least 1 and pageSize between 1 and {maxPageSize}.");

    public static IResult Unauthorized(HttpContext context) =>
        Create(context, "unauthorized", StatusCodes.Status401Unauthorized, "A valid access token is required.");

    private static IResult Create(HttpContext context, string type, int status, string detail) =>
        Results.Problem(
            type: type,
            statusCode: status,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["correlationId"] = CorrelationIdMiddleware.From(context) });
}
