using Chubb.Claims.Application;

namespace Chubb.Claims.Api;

public static class StoredResponses
{
    private const string DefaultContentType = "application/json";

    // Runs the result against a scratch response so the first answer and every replay are the same bytes.
    public static async Task<StoredResponse> CaptureAsync(IResult result, HttpContext original)
    {
        await using var body = new MemoryStream();
        var scratch = new DefaultHttpContext { RequestServices = original.RequestServices };
        scratch.Response.Body = body;
        await result.ExecuteAsync(scratch);

        var location = scratch.Response.Headers.Location.ToString();
        return new StoredResponse(
            scratch.Response.StatusCode,
            body.ToArray(),
            scratch.Response.ContentType ?? DefaultContentType,
            string.IsNullOrEmpty(location) ? null : location);
    }
}
