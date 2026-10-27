using System.Security.Cryptography;
using Chubb.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Chubb.Notification.Api;

public static class AuthenticationSetup
{
    public const string AuthenticatedPolicy = "authenticated";

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddServiceAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtValidationOptions>()
            .BindConfiguration(JwtValidationOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => File.Exists(options.PublicKeyPath), "Jwt:PublicKeyPath does not point to a file.")
            .ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtValidationOptions>>(ConfigureValidation);
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build())
            .AddPolicy(AuthenticatedPolicy, policy => policy.RequireAuthenticatedUser());
        return services;
    }

    private static void ConfigureValidation(JwtBearerOptions bearer, IOptions<JwtValidationOptions> jwtOptions)
    {
        var settings = jwtOptions.Value;
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(settings.PublicKeyPath));

        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            IssuerSigningKey = new RsaSecurityKey(rsa),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = ClockSkew,
            NameClaimType = ClaimNames.Name,
            RoleClaimType = ClaimNames.Role,
        };
    }
}
