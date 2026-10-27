using Chubb.Identity.Application;
using FluentValidation;

namespace Chubb.Identity.Api;

public static class AuthEndpoints
{
    private const string BearerTokenType = "Bearer";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/auth/token", CreateTokenAsync)
            .AllowAnonymous()
            .WithName("CreateToken")
            .WithTags("Auth")
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);
        return routes;
    }

    private static async Task<IResult> CreateTokenAsync(
        LoginRequest request,
        IValidator<LoginCommand> validator,
        LoginHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.Email, request.Password);
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary(), type: ProblemTypes.ValidationError);
        }

        var result = await handler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: result.Error.Message,
                type: result.Error.Code);
        }

        var token = result.Value;
        return Results.Ok(new TokenResponse(token.Value, BearerTokenType, token.ExpiresInSeconds));
    }
}
