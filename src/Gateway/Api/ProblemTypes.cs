namespace Chubb.Gateway.Api;

public static class ProblemTypes
{
    private static readonly Dictionary<int, (string Type, string Title)> ByStatus = new()
    {
        [StatusCodes.Status400BadRequest] = ("bad-request", "The request is malformed."),
        [StatusCodes.Status401Unauthorized] = ("unauthenticated", "A valid access token is required."),
        [StatusCodes.Status403Forbidden] = ("forbidden", "The access token does not allow this request."),
        [StatusCodes.Status404NotFound] = ("not-found", "The resource was not found."),
        [StatusCodes.Status413PayloadTooLarge] = ("payload-too-large", "The request body is too large."),
        [StatusCodes.Status500InternalServerError] = ("internal-error", "An unexpected error occurred."),
        [StatusCodes.Status501NotImplemented] = ("not-implemented", "This capability is not available yet."),
        [StatusCodes.Status502BadGateway] = ("upstream-unavailable", "The upstream service is unavailable."),
        [StatusCodes.Status503ServiceUnavailable] = ("upstream-unavailable", "The upstream service is unavailable."),
        [StatusCodes.Status504GatewayTimeout] = ("upstream-unavailable", "The upstream service did not respond in time."),
    };

    public static void Apply(Microsoft.AspNetCore.Mvc.ProblemDetails problem)
    {
        if (problem.Status is not { } status || !ByStatus.TryGetValue(status, out var known))
        {
            return;
        }

        if (problem.Type is null || problem.Type.StartsWith("https://", StringComparison.Ordinal))
        {
            problem.Type = known.Type;
            problem.Title = known.Title;
        }
    }
}
