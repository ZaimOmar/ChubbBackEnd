using Microsoft.AspNetCore.Http.Features;

namespace Chubb.Identity.Api;

public sealed class RequestBodyLimitMiddleware(RequestDelegate next, long maxBytes)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength > maxBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return Task.CompletedTask;
        }

        // Covers chunked bodies without a Content-Length; not every server supports the feature.
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = maxBytes;
        }

        return next(context);
    }
}
