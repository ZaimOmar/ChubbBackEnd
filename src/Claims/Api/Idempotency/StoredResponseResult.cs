using Chubb.Claims.Application;
using Chubb.Contracts;

namespace Chubb.Claims.Api;

public sealed class StoredResponseResult(StoredResponse response, bool isReplay) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = response.StatusCode;
        httpContext.Response.ContentType = response.ContentType;
        if (response.Location is not null)
        {
            httpContext.Response.Headers.Location = response.Location;
        }

        if (isReplay)
        {
            httpContext.Response.Headers[HeaderNames.IdempotentReplayed] = "true";
        }

        await httpContext.Response.Body.WriteAsync(response.Body, httpContext.RequestAborted);
    }
}
