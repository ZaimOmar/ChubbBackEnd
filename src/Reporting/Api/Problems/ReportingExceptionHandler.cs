using Microsoft.AspNetCore.Diagnostics;

namespace Chubb.Reporting.Api.Problems;

public sealed class ReportingExceptionHandler(ILogger<ReportingExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError("Request failed with {ErrorType}", exception.GetType().Name);
        await ReportingProblems.ServerError(httpContext).ExecuteAsync(httpContext);
        return true;
    }
}
