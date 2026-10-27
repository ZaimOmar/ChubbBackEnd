using System.Security.Cryptography;
using Chubb.Identity.Application;
using Chubb.Identity.Infrastructure;
using Chubb.Identity.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace Chubb.Identity.Api;

public static class ServiceRegistration
{
    public const string DatabaseConnectionName = "IdentityDb";

    public static IServiceCollection AddIdentityServices(this IServiceCollection services, bool isDevelopment)
    {
        services.AddJwtIssuing();
        services.AddAuthentication();
        // Default deny: an endpoint without an explicit policy needs an authenticated user. Identity validates
        // no tokens itself, so such an endpoint fails closed until a scheme is added deliberately.
        services.AddAuthorizationBuilder().SetFallbackPolicy(
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
        services.AddDbContext<IdentityDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString(DatabaseConnectionName)));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PasswordVerifier>();
        services.AddSingleton<IPasswordVerifier>(provider => provider.GetRequiredService<PasswordVerifier>());
        services.AddSingleton<ITokenIssuer, TokenIssuer>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<LoginHandler>();
        services.AddSingleton<IValidator<LoginCommand>, LoginValidator>();
        services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>();
        if (isDevelopment)
        {
            services.AddScoped<IdentitySeeder>();
            services.AddScoped<DatabaseInitializer>();
            services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName)
                .ValidateDataAnnotations().ValidateOnStart();
        }

        return services;
    }

    public static IServiceCollection AddIdentityProblemDetails(this IServiceCollection services)
    {
        services.AddExceptionHandler<BadRequestExceptionHandler>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            ProblemTypes.Apply(context.ProblemDetails);
            if (context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
            {
                context.ProblemDetails.Extensions["correlationId"] = correlationId;
            }
        });
        return services;
    }

    public static IServiceCollection AddIdentityOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Chubb Identity API", Version = "v1" });
            options.CustomSchemaIds(type => $"Identity{type.Name}");
            options.SupportNonNullableReferenceTypes();
            options.NonNullableReferenceTypesAsRequired();
        });
        return services;
    }

    private static void AddJwtIssuing(this IServiceCollection services)
    {
        services.AddOptions<JwtIssuerOptions>()
            .BindConfiguration(JwtIssuerOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => File.Exists(options.PrivateKeyPath), "Jwt:PrivateKeyPath does not point to a file.")
            .ValidateOnStart();
        services.AddSingleton(provider =>
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(provider.GetRequiredService<IOptions<JwtIssuerOptions>>().Value.PrivateKeyPath));
            return new RsaSecurityKey(rsa);
        });
    }
}
