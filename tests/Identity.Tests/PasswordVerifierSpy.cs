using System.Collections.Concurrent;
using Chubb.Identity.Application;

namespace Chubb.Identity.Tests;

public sealed class PasswordVerifierSpy
{
    private readonly ConcurrentQueue<bool> _hashWasNull = new();

    public IReadOnlyCollection<bool> Calls => _hashWasNull.ToArray();

    public IPasswordVerifier Wrap(IPasswordVerifier inner) => new SpyingVerifier(inner, _hashWasNull);

    private sealed class SpyingVerifier(IPasswordVerifier inner, ConcurrentQueue<bool> calls) : IPasswordVerifier
    {
        public bool Verify(string? passwordHash, string password)
        {
            calls.Enqueue(passwordHash is null);
            return inner.Verify(passwordHash, password);
        }
    }
}
