using Chubb.Identity.Domain;
using Microsoft.Extensions.Logging;

namespace Chubb.Identity.Application;

public sealed class LoginHandler(
    IUserRepository users,
    IPasswordVerifier passwordVerifier,
    ITokenIssuer tokenIssuer,
    ILogger<LoginHandler> logger)
{
    public async Task<Result<AccessToken>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(User.NormalizeEmail(command.Email), cancellationToken);

        // Always verify, even for an unknown email, so the failure path costs the same.
        var passwordMatches = passwordVerifier.Verify(user?.PasswordHash, command.Password);
        if (user is null || !passwordMatches)
        {
            logger.LogInformation("Login failed");
            return Result<AccessToken>.Failure(LoginErrors.InvalidCredentials);
        }

        logger.LogInformation("Token issued for user {UserId}", user.Id);
        return Result<AccessToken>.Success(tokenIssuer.Issue(user));
    }
}
