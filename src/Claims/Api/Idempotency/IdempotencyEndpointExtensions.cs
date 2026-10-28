namespace Chubb.Claims.Api;

public static class IdempotencyEndpointExtensions
{
    public static RouteHandlerBuilder WithIdempotency(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(new IdempotencyMetadata()).AddEndpointFilter<IdempotencyFilter>();
}
