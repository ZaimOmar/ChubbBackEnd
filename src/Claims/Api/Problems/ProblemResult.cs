using System.Text.Json;

namespace Chubb.Claims.Api.Problems;

public sealed class ProblemResult(ProblemDocument document, string? retryAfter) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = document.Status;
        httpContext.Response.ContentType = ProblemDocument.ContentType;
        if (retryAfter is not null)
        {
            httpContext.Response.Headers.RetryAfter = retryAfter;
        }

        await JsonSerializer.SerializeAsync(httpContext.Response.Body, document, ClaimsJson.Options, httpContext.RequestAborted);
    }
}
