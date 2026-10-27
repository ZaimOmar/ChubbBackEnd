namespace Chubb.Gateway.Api;

public static class GatewayProblemDetails
{
    public static IServiceCollection AddGatewayProblemDetails(this IServiceCollection services)
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
}
