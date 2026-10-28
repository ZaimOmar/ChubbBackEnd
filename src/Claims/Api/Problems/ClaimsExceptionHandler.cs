using Chubb.Claims.Application;
using Microsoft.AspNetCore.Diagnostics;

namespace Chubb.Claims.Api.Problems;

public sealed class ClaimsExceptionHandler(ILogger<ClaimsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = CorrelationIdMiddleware.From(httpContext);
        var problem = exception switch
        {
            DomainRefusalException refusal => ProblemResults.FromRefusal(refusal.Refusal, correlationId),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => ProblemResults.PayloadTooLarge(correlationId),
            BadHttpRequestException => ProblemResults.MalformedBody(correlationId),
            UnauthorizedAccessException => ProblemResults.Unauthorized(correlationId),
            _ => ProblemResults.ServerError(correlationId),
        };

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled error, correlation {CorrelationId}", correlationId);
        }

        await problem.ToResult().ExecuteAsync(httpContext);
        return true;
    }
}
