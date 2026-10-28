namespace Chubb.Claims.Application;

public sealed record StoredResponse(int StatusCode, byte[] Body, string ContentType, string? Location);
