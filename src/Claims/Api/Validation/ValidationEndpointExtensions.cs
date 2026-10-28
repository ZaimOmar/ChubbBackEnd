namespace Chubb.Claims.Api;

public static class ValidationEndpointExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class =>
        builder.AddEndpointFilter<RouteHandlerBuilder, ValidationFilter<TRequest>>();
}
