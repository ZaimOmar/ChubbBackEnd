using Chubb.Identity.Domain;

namespace Chubb.Identity.Application;

public static class LoginErrors
{
    public static readonly DomainError InvalidCredentials = new("invalid-credentials", "The email or password is incorrect.");
}
