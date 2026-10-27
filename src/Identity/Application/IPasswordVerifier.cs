namespace Chubb.Identity.Application;

public interface IPasswordVerifier
{
    /// <summary>
    /// Verifies the password; a null hash (unknown user) is checked against a dummy hash so timing does not reveal it.
    /// </summary>
    bool Verify(string? passwordHash, string password);
}
