using Chubb.Identity.Application;
using Microsoft.AspNetCore.Identity;

namespace Chubb.Identity.Infrastructure;

public sealed class PasswordVerifier : IPasswordVerifier
{
    private const string DummyPassword = "dummy-password-for-unknown-users";

    private readonly PasswordHasher<string> _hasher = new();
    private readonly string _dummyHash;

    public PasswordVerifier()
    {
        _dummyHash = Hash(DummyPassword);
    }

    public string Hash(string password) => _hasher.HashPassword(string.Empty, password);

    public bool Verify(string? passwordHash, string password)
    {
        var result = _hasher.VerifyHashedPassword(string.Empty, passwordHash ?? _dummyHash, password);
        return passwordHash is not null && result != PasswordVerificationResult.Failed;
    }
}
