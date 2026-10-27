using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Chubb.TestSupport;

public static class TestTokens
{
    public const string Issuer = "chubb-identity";
    public const string Audience = "chubb-claims-api";

    public static string Create(
        SecurityKey signingKey,
        string[]? roles = null,
        string? singleRole = null,
        string issuer = Issuer,
        string audience = Audience,
        TimeSpan? lifetime = null,
        string algorithm = SecurityAlgorithms.RsaSha256)
    {
        var now = DateTime.UtcNow;
        var expires = now + (lifetime ?? TimeSpan.FromMinutes(30));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now.AddHours(-2),
            NotBefore = now.AddHours(-2),
            Expires = expires,
            SigningCredentials = new SigningCredentials(signingKey, algorithm),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString(),
                ["name"] = "Test User",
                ["market"] = "SG",
                ["role"] = singleRole is not null ? singleRole : roles ?? ["Officer"],
            },
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static string CreateWithoutSignature()
    {
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64UrlEncoder.Encode(
            $$"""{"sub":"{{Guid.NewGuid()}}","role":["Manager"],"iss":"{{Issuer}}","aud":"{{Audience}}","exp":{{DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()}}}""");
        return $"{header}.{payload}.";
    }

    public static string CreateHmacSignedWithKeyMaterial(string publicKeyPem) =>
        Create(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(publicKeyPem)), algorithm: SecurityAlgorithms.HmacSha256);
}
