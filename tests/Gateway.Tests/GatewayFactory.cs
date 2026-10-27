using Chubb.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Chubb.Gateway.Tests;

public sealed class GatewayFactory(
    string downstreamAddress,
    TestKeyPair keys,
    string environment = "Development",
    string? contractsPath = null,
    string allowedOrigin = GatewayFactory.AllowedOrigin) : WebApplicationFactory<Program>
{
    public const string AllowedOrigin = "http://localhost:4200";

    private static readonly string[] ClusterIds = ["identity", "claims", "reporting", "notification"];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Jwt:PublicKeyPath"] = keys.PublicKeyPath,
                ["Cors:AllowedOrigins:0"] = allowedOrigin,
                ["OpenApi:ContractsPath"] = contractsPath ?? RepositoryPaths.ContractsDirectory,
            };
            foreach (var cluster in ClusterIds)
            {
                settings[$"ReverseProxy:Clusters:{cluster}:Destinations:primary:Address"] = downstreamAddress;
            }

            configuration.AddInMemoryCollection(settings);
        });
    }
}
