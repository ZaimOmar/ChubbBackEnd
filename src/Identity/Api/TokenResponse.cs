namespace Chubb.Identity.Api;

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
