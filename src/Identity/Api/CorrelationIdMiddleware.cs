using Serilog.Context;
using CorrelationHeader = Chubb.Contracts.HeaderNames;

namespace Chubb.Identity.Api;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request);
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeader.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("correlationId", correlationId))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpRequest request)
    {
        var inbound = request.Headers[CorrelationHeader.CorrelationId].ToString();
        var id = Guid.TryParse(inbound, out var parsed) ? parsed : Guid.NewGuid();
        return id.ToString();
    }
}
