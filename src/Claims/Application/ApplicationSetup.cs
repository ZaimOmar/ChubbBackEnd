namespace Chubb.Claims.Application;

public static class ApplicationSetup
{
    public static IServiceCollection AddClaimsCommands(this IServiceCollection services)
    {
        services.AddOptions<IdempotencyOptions>()
            .BindConfiguration(IdempotencyOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.Retention > TimeSpan.Zero, "Idempotency:Retention must be positive.")
            .ValidateOnStart();
        services.AddScoped<SubmitClaimHandler>();
        services.AddScoped<PickUpClaimHandler>();
        services.AddScoped<AssessClaimHandler>();
        services.AddScoped<ProposeDecisionHandler>();
        services.AddScoped<ConfirmDecisionHandler>();
        services.AddScoped<SendBackDecisionHandler>();
        services.AddScoped<IdempotentCommandExecutor>();
        return services;
    }
}
