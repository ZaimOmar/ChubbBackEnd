namespace Chubb.Gateway.Tests;

public sealed record ReceivedRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers);
