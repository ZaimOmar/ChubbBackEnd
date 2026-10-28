namespace Chubb.Claims.Api;

// Only idempotent endpoints need the body twice: once for binding, once for the fingerprint.
public sealed class RequestBufferingMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>() is not null)
        {
            context.Request.EnableBuffering();
        }

        return next(context);
    }
}
