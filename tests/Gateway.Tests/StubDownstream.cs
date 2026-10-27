using System.Collections.Concurrent;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Chubb.Gateway.Tests;

public sealed class StubDownstream : IAsyncDisposable
{
    private readonly ConcurrentQueue<ReceivedRequest> _requests;
    private readonly WebApplication _app;

    private StubDownstream(WebApplication app, ConcurrentQueue<ReceivedRequest> requests)
    {
        _app = app;
        _requests = requests;
        Address = app.Urls.Single();
    }

    public string Address { get; }

    public IReadOnlyCollection<ReceivedRequest> Requests => _requests.ToArray();

    public static async Task<StubDownstream> StartAsync()
    {
        var requests = new ConcurrentQueue<ReceivedRequest>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Run(context =>
        {
            var headers = context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString());
            requests.Enqueue(new ReceivedRequest(context.Request.Method, context.Request.Path, headers));
            return IsManagerOnly(context.Request) && !HasManagerRole(context.Request)
                ? WriteForbidden(context.Response)
                : context.Response.WriteAsJsonAsync(new { ok = true });
        });
        await app.StartAsync();
        return new StubDownstream(app, requests);
    }

    // Stands in for a service's role policy: the Gateway must pass its 403 through untouched (AC-4).
    private static bool IsManagerOnly(HttpRequest request) => request.Path.StartsWithSegments("/dashboard");

    private static bool HasManagerRole(HttpRequest request)
    {
        var token = request.Headers.Authorization.ToString()["Bearer ".Length..];
        return new JsonWebToken(token).GetPayloadValue<string[]>("role").Contains("Manager");
    }

    private static Task WriteForbidden(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status403Forbidden;
        response.ContentType = "application/problem+json";
        return response.WriteAsync("""{"type":"role-not-allowed","title":"Stub service refused the role.","status":403}""");
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
