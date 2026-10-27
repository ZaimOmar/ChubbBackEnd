using Chubb.Identity.Infrastructure;

namespace Chubb.Identity.Tests.Infrastructure;

public sealed class PasswordVerifierTests
{
    private readonly PasswordVerifier _verifier = new();

    [Fact]
    public void PasswordVerifier_Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _verifier.Hash("correct-password");

        _verifier.Verify(hash, "correct-password").Should().BeTrue();
    }

    [Fact]
    public void PasswordVerifier_Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _verifier.Hash("correct-password");

        _verifier.Verify(hash, "wrong-password").Should().BeFalse();
    }

    [Fact]
    public void PasswordVerifier_Verify_UnknownUserHash_ReturnsFalse()
    {
        _verifier.Verify(null, "dummy-password-for-unknown-users").Should().BeFalse();
    }
}
