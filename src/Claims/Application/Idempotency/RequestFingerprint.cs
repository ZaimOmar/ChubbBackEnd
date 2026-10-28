using System.Security.Cryptography;
using System.Text;

namespace Chubb.Claims.Application;

public static class RequestFingerprint
{
    private const byte Separator = 0;

    // Raw body bytes, no canonicalisation: a resend must be byte-identical (BR-81).
    public static byte[] Compute(string method, string route, ReadOnlySpan<byte> body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(method));
        hash.AppendData([Separator]);
        hash.AppendData(Encoding.UTF8.GetBytes(route));
        hash.AppendData([Separator]);
        hash.AppendData(body);
        return hash.GetHashAndReset();
    }
}
