using Chubb.Identity.Application;
using Chubb.Identity.Infrastructure;
using Chubb.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Chubb.Identity.Tests;

public sealed class IdentityApiFactory(string connectionString, TestKeyPair keys, string environment = "Development") : WebApplicationFactory<Program>
{
    public const string SeedPassword = "Dev-Only-Seed-Password-1";

    public CapturingLogSink Logs { get; } = new();

    public PasswordVerifierSpy VerifierSpy { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDb"] = connectionString,
                ["Jwt:PrivateKeyPath"] = keys.PrivateKeyPath,
                ["Identity:SeedPassword"] = SeedPassword,
            }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogEventSink>(Logs);
            services.AddSingleton<IPasswordVerifier>(provider =>
                VerifierSpy.Wrap(provider.GetRequiredService<PasswordVerifier>()));
        });
    }
}
