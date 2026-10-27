namespace Chubb.Identity.Api;

public static class ProblemTypes
{
    public const string ValidationError = "validation-error";

    private static readonly Dictionary<int, (string Type, string Title)> ByStatus = new()
    {
        [StatusCodes.Status400BadRequest] = ("bad-request", "The request is malformed."),
        [StatusCodes.Status404NotFound] = ("not-found", "The resource was not found."),
        [StatusCodes.Status405MethodNotAllowed] = ("method-not-allowed", "The method is not allowed."),
        [StatusCodes.Status413PayloadTooLarge] = ("payload-too-large", "The request body is too large."),
        [StatusCodes.Status500InternalServerError] = ("internal-error", "An unexpected error occurred."),
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
