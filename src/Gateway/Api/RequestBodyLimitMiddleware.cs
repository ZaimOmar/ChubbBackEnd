using Microsoft.AspNetCore.Http.Features;

namespace Chubb.Gateway.Api;

public sealed class RequestBodyLimitMiddleware(RequestDelegate next)
{
    public const long ProxiedMaxBytes = 1_048_576;
    public const long AuthMaxBytes = 4096;

    private static readonly PathString AuthPath = new("/auth");

    public Task InvokeAsync(HttpContext context)
    {
        var maxBytes = context.Request.Path.StartsWithSegments(AuthPath) ? AuthMaxBytes : ProxiedMaxBytes;
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
