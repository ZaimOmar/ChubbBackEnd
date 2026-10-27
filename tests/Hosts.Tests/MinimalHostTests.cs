using System.Net;
using Chubb.TestSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Chubb.Hosts.Tests;

public sealed class MinimalHostTests : IDisposable
{
    private readonly TestKeyPair _keys = new();

    public void Dispose() => _keys.Dispose();

    [Fact]
    public async Task ClaimsHost_Health_IsAnonymousAndEndpointsHavePolicy() =>
        await AssertHost<Claims.Api.JwtValidationOptions>();

    [Fact]
    public async Task ReportingHost_Health_IsAnonymousAndEndpointsHavePolicy() =>
        await AssertHost<Reporting.Api.JwtValidationOptions>();

    [Fact]
    public async Task NotificationHost_Health_IsAnonymousAndEndpointsHavePolicy() =>
        await AssertHost<Notification.Api.JwtValidationOptions>();

    // Any public type of the host assembly locates its entry point; the hosts expose no Program type.
    private async Task AssertHost<TAssemblyMarker>()
        where TAssemblyMarker : class
    {
        await using var factory = new WebApplicationFactory<TAssemblyMarker>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Jwt:PublicKeyPath"] = _keys.PublicKeyPath })));
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        EndpointPolicies.FindEndpointsWithoutExplicitPolicy(factory.Services).Should().BeEmpty();
        await AssertFallbackRequiresAuthenticatedUser(factory.Services);
        AssertBearerValidation(factory.Services);
    }

    private static async Task AssertFallbackRequiresAuthenticatedUser(IServiceProvider services)
    {
        var fallback = await services.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();

        fallback.Should().NotBeNull();
        fallback!.Requirements.Should().ContainSingle(requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    private static void AssertBearerValidation(IServiceProvider services)
    {
        var bearer = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        bearer.MapInboundClaims.Should().BeFalse();
        bearer.TokenValidationParameters.ValidAlgorithms.Should().Equal(SecurityAlgorithms.RsaSha256);
        bearer.TokenValidationParameters.ValidIssuer.Should().Be(TestTokens.Issuer);
        bearer.TokenValidationParameters.ValidAudience.Should().Be(TestTokens.Audience);
        bearer.TokenValidationParameters.RequireSignedTokens.Should().BeTrue();
    }
}
