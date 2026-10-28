using Chubb.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Chubb.Claims.Api;

public static class ClaimsPolicies
{
    public const string Claimant = "claims-claimant";
    public const string Officer = "claims-officer";
    public const string Manager = "claims-manager";
    public const string Staff = "claims-staff";
    public const string AnyUser = "claims-any-user";

    public static IServiceCollection AddClaimsAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Claimant, policy => policy.RequireRole(Roles.Claimant))
            .AddPolicy(Officer, policy => policy.RequireRole(Roles.Officer))
            .AddPolicy(Manager, policy => policy.RequireRole(Roles.Manager))
            .AddPolicy(Staff, policy => policy.RequireRole(Roles.Officer, Roles.Manager))
            .AddPolicy(AnyUser, policy => policy.RequireRole(Roles.Claimant, Roles.Officer, Roles.Manager));
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ForbiddenResultHandler>();
        return services;
    }
}
