namespace Chubb.Gateway.Api;

public static class PayeeDetailsRoute
{
    public const string Pattern = "/claims/{claimId}/payee-details";

    // Bank details belong to Finance only (BR-42). Finance is Phase 2, so until it is built this literal route
    // outranks the /claims catch-all and answers 501 for every method, keeping the request away from Claims.
    public static IEndpointRouteBuilder MapPayeeDetailsNotImplemented(this IEndpointRouteBuilder endpoints)
    {
        endpoints.Map(Pattern, () => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .RequireAuthorization(AuthenticationSetup.AuthenticatedPolicy);
        return endpoints;
    }
}
