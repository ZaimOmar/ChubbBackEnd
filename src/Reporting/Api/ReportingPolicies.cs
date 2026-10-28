using Chubb.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Chubb.Reporting.Api;

public static class ReportingPolicies
{
    public const string Manager = "reporting-manager";
    public const string Staff = "reporting-staff";

    public static IServiceCollection AddReportingAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Manager, policy => policy.RequireRole(Roles.Manager))
            .AddPolicy(Staff, policy => policy.RequireRole(Roles.Officer, Roles.Manager));
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
        return services;
    }
}
