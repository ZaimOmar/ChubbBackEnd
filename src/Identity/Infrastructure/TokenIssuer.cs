using Chubb.Contracts;
using Chubb.Identity.Application;
using Chubb.Identity.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Chubb.Identity.Infrastructure;

public sealed class TokenIssuer(IOptions<JwtIssuerOptions> options, RsaSecurityKey signingKey, TimeProvider timeProvider)
    : ITokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials = new(signingKey, SecurityAlgorithms.RsaSha256);

    public AccessToken Issue(User user)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(settings.LifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>
            {
                [ClaimNames.Subject] = user.Id.ToString(),
                [ClaimNames.Name] = user.DisplayName,
                [ClaimNames.Market] = user.Market,
                [ClaimNames.Role] = user.Roles.Select(role => role.ToString()).ToArray(),
                [ClaimNames.TokenId] = Guid.NewGuid().ToString(),
            },
        };

        return new AccessToken(_handler.CreateToken(descriptor), (int)lifetime.TotalSeconds);
    }
}
