using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Chubb.TestSupport;

public sealed class TestKeyPair : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly string _directory = Directory.CreateTempSubdirectory("chubb-keys-").FullName;

    public TestKeyPair()
    {
        PrivateKeyPath = Path.Combine(_directory, "jwt-private.pem");
        PublicKeyPath = Path.Combine(_directory, "jwt-public.pem");
        PublicKeyPem = _rsa.ExportSubjectPublicKeyInfoPem();
        File.WriteAllText(PrivateKeyPath, _rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(PublicKeyPath, PublicKeyPem);
        SigningKey = new RsaSecurityKey(_rsa);
    }

    public string PrivateKeyPath { get; }

    public string PublicKeyPath { get; }

    public string PublicKeyPem { get; }

    public RsaSecurityKey SigningKey { get; }

    public void Dispose()
    {
        _rsa.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
