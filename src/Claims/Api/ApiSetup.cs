using Chubb.Claims.Api.Problems;
using FluentValidation;
using Microsoft.AspNetCore.Routing;

namespace Chubb.Claims.Api;

public static class ApiSetup
{
    public const long MaxRequestBodyBytes = 64 * 1024;

    public static IServiceCollection AddClaimsApi(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options => ClaimsJson.Configure(options.SerializerOptions));
        // Bad bodies must reach ClaimsExceptionHandler so they become problem documents, not framework 400s.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        services.AddExceptionHandler<ClaimsExceptionHandler>();
        services.AddProblemDetails();
        services.AddScoped<CommandContextFactory>();
        services.AddSingleton<IValidator<SubmitClaimRequest>, SubmitClaimRequestValidator>();
        services.AddSingleton<IValidator<AssessedAmountRequest>, AssessedAmountRequestValidator>();
        services.AddSingleton<IValidator<ProposeDecisionRequest>, ProposeDecisionRequestValidator>();
        services.AddSingleton<IValidator<SendBackRequest>, SendBackRequestValidator>();
        services.AddSingleton<IValidator<ClaimListQuery>, ClaimListQueryValidator>();
        services.AddClaimsAuthorization();
        return services;
    }
}
