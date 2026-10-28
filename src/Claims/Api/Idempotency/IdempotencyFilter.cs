using System.Text.RegularExpressions;
using Chubb.Claims.Api.Problems;
using Chubb.Claims.Application;

namespace Chubb.Claims.Api;

// Thin HTTP adapter over IdempotentCommandExecutor (ADR-009): key, scope and fingerprint in; stored bytes out.
public sealed partial class IdempotencyFilter(IdempotentCommandExecutor executor) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var correlationId = CorrelationIdMiddleware.From(http);
        var (key, problem) = IdempotencyKeyReader.Read(http.Request.Headers);
        if (problem != IdempotencyKeyReader.KeyProblem.None)
        {
            return KeyProblem(problem, correlationId);
        }

        var actor = ActorFactory.TryCreate(http.User) ?? throw new UnauthorizedAccessException();
        var route = CanonicalRoute(http);
        var request = new IdempotencyRequest(actor.UserId, route, key!, await FingerprintAsync(http, route));
        var outcome = await executor.ExecuteAsync(
            request,
            async _ => await StoredResponses.CaptureAsync(AsResult(await next(context)), http),
            refusal => ProblemResults.FromRefusal(refusal, correlationId).ToStoredResponse(),
            http.RequestAborted);
        return ToResult(outcome, correlationId);
    }

    private static IResult ToResult(IdempotencyOutcome outcome, string correlationId) => outcome switch
    {
        IdempotencyOutcome.Executed executed => new StoredResponseResult(executed.Response, isReplay: false),
        IdempotencyOutcome.NotStored notStored => new StoredResponseResult(notStored.Response, isReplay: false),
        IdempotencyOutcome.Replayed replayed => new StoredResponseResult(replayed.Response, isReplay: true),
        IdempotencyOutcome.InProgress => ProblemResults.IdempotencyRequestInProgress(correlationId)
            .ToResult(ProblemResults.RetryAfterSeconds),
        IdempotencyOutcome.KeyReused => ProblemResults.IdempotencyKeyReused(correlationId).ToResult(),
        _ => throw new InvalidOperationException("Unknown idempotency outcome."),
    };

    private static IResult KeyProblem(IdempotencyKeyReader.KeyProblem problem, string correlationId) =>
        problem == IdempotencyKeyReader.KeyProblem.Missing
            ? ProblemResults.IdempotencyKeyMissing(correlationId).ToResult()
            : ProblemResults.IdempotencyKeyInvalid(correlationId).ToResult();

    private static IResult AsResult(object? value) => value as IResult ?? Results.Ok(value);

    private static async Task<byte[]> FingerprintAsync(HttpContext http, string route)
    {
        var body = http.Request.Body;
        body.Position = 0;
        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, http.RequestAborted);
        body.Position = 0;
        return RequestFingerprint.Compute(http.Request.Method, route, buffer.ToArray());
    }

    // Method plus the route template filled with resolved values (GUIDs lower-case), never the raw path.
    private static string CanonicalRoute(HttpContext http)
    {
        var template = ((RouteEndpoint)http.GetEndpoint()!).RoutePattern.RawText ?? string.Empty;
        var filled = Placeholder().Replace(template, match =>
        {
            var value = http.GetRouteValue(match.Groups["name"].Value);
            return value is Guid guid ? guid.ToString("D") : value?.ToString() ?? match.Value;
        });
        return $"{http.Request.Method} {filled}";
    }

    [GeneratedRegex(@"\{(?<name>\w+)(:\w+)?\}")]
    private static partial Regex Placeholder();
}
