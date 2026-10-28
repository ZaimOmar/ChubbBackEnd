namespace Chubb.Reporting.Api.Problems;

// The single place where errors become RFC 7807 documents; details are fixed texts, never submitted values.
public static class ReportingProblems
{
    public static IResult Unauthorized(HttpContext context) =>
        Create(context, "unauthorized", StatusCodes.Status401Unauthorized, "A valid access token is required.");

    public static IResult Forbidden(HttpContext context) =>
        Create(context, "forbidden", StatusCodes.Status403Forbidden, "The caller's role does not allow this request.");

    public static IResult ServerError(HttpContext context) =>
        Create(context, "server-error", StatusCodes.Status500InternalServerError, "An unexpected error occurred.");

    public static IResult Validation(HttpContext context, IDictionary<string, string[]> errors) =>
        Results.Problem(
            title: "Bad Request",
            detail: "One or more fields are invalid.",
            statusCode: StatusCodes.Status400BadRequest,
            type: "validation-error",
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = CorrelationIdMiddleware.From(context),
                ["errors"] = errors,
            });

    private static IResult Create(HttpContext context, string type, int status, string detail) =>
        Results.Problem(
            title: Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status),
            detail: detail,
            statusCode: status,
            type: type,
            extensions: new Dictionary<string, object?> { ["correlationId"] = CorrelationIdMiddleware.From(context) });
}
