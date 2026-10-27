using Chubb.Contracts;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace Chubb.Gateway.Api;

public static class CorsSetup
{
    public const string FrontendPolicy = "frontend";

    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    private static readonly string[] AllowedHeaders =
        ["Authorization", "Content-Type", HeaderNames.IdempotencyKey, HeaderNames.CorrelationId];

    public static IServiceCollection AddFrontendCors(this IServiceCollection services)
    {
        services.AddOptions<CorsSettings>()
            .BindConfiguration(CorsSettings.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                settings => settings.AllowedOrigins.All(IsAbsoluteHttpOrigin),
                "Cors:AllowedOrigins must be absolute http(s) origins without a path; no wildcard and no 'null'.")
            .ValidateOnStart();
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<CorsSettings>>((cors, settings) =>
            cors.AddPolicy(FrontendPolicy, policy => policy
                .WithOrigins(settings.Value.AllowedOrigins)
                .WithMethods(AllowedMethods)
                .WithHeaders(AllowedHeaders)
                .WithExposedHeaders(HeaderNames.CorrelationId)));
        return services;
    }

    private static bool IsAbsoluteHttpOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.GetLeftPart(UriPartial.Authority) == origin;
}
