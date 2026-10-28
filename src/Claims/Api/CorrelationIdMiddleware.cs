using Chubb.Contracts;

namespace Chubb.Claims.Api;

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request);
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderNames.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["correlationId"] = correlationId }))
        {
            await next(context);
        }
    }

    public static string From(HttpContext context) =>
        context.Items[ItemKey] as string ?? Guid.NewGuid().ToString();

    private static string ResolveCorrelationId(HttpRequest request)
    {
        var inbound = request.Headers[HeaderNames.CorrelationId].ToString();
        return (Guid.TryParse(inbound, out var parsed) ? parsed : Guid.NewGuid()).ToString();
    }
}
