using Chubb.Contracts;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Chubb.Gateway.Api;

public static class ProxyTransforms
{
    // An allowlist, not a denylist: any header not named here (X-User-*, X-Forwarded-User, Remote-User, ...)
    // never reaches a service, so no identity-like header can be trusted by accident. The token is the only identity.
    private static readonly HashSet<string> ForwardedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Accept", "Accept-Encoding", "Accept-Language", "Cache-Control", "If-Match",
        "If-None-Match", "If-Modified-Since", "If-Unmodified-Since", "Pragma", "Range", "User-Agent",
        "Traceparent", "Tracestate", "Baggage", HeaderNames.IdempotencyKey,
    };

    public static void Configure(TransformBuilderContext context)
    {
        context.AddRequestTransform(transform =>
        {
            var headers = transform.ProxyRequest.Headers;
            foreach (var name in headers.Select(header => header.Key).Where(name => !ForwardedHeaders.Contains(name)).ToList())
            {
                headers.Remove(name);
            }

            if (transform.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
            {
                headers.TryAddWithoutValidation(HeaderNames.CorrelationId, correlationId);
            }

            return ValueTask.CompletedTask;
        });
    }
}
