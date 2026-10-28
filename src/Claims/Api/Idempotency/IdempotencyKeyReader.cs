using System.Text.RegularExpressions;
using Chubb.Contracts;

namespace Chubb.Claims.Api;

public static partial class IdempotencyKeyReader
{
    public enum KeyProblem
    {
        None,
        Missing,
        Invalid,
    }

    public static (string? Key, KeyProblem Problem) Read(IHeaderDictionary headers)
    {
        var values = headers[HeaderNames.IdempotencyKey];
        return values.Count switch
        {
            0 => (null, KeyProblem.Missing),
            1 when KeyPattern().IsMatch(values[0] ?? string.Empty) => (values[0], KeyProblem.None),
            _ => (null, KeyProblem.Invalid),
        };
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{8,128}$")]
    private static partial Regex KeyPattern();
}
