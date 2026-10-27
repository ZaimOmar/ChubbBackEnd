namespace Chubb.Identity.Application;

public sealed record AccessToken(string Value, int ExpiresInSeconds);
