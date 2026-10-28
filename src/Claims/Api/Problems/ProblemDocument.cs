using System.Text.Json;
using Chubb.Claims.Application;

namespace Chubb.Claims.Api.Problems;

public sealed record ProblemDocument(
    string Type, string Title, int Status, string Detail, string CorrelationId, IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public const string ContentType = "application/problem+json";

    public IResult ToResult(string? retryAfter = null) => new ProblemResult(this, retryAfter);

    public StoredResponse ToStoredResponse() =>
        new(Status, JsonSerializer.SerializeToUtf8Bytes(this, ClaimsJson.Options), ContentType, null);
}
