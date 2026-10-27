using System.Net;
using System.Text.Json;
using Chubb.TestSupport;
using Microsoft.IdentityModel.Tokens;

namespace Chubb.Gateway.Tests;

public sealed class GatewayAuthenticationTests : GatewayTestBase
{
    private static readonly string[] SpoofedHeaders =
    [
        "X-User-Id",
        "X-User-Role",
        "X-Actor-Id",
        "X-Claimant-Id",
        "X-Officer-Id",
        "X-Sub",
        "Remote-User",
        "X-Remote-User",
        "X-Forwarded-User",
        "X-Auth-Request-User",
        "X-Auth-Request-Email",
        "X-MS-CLIENT-PRINCIPAL",
        "X-MS-CLIENT-PRINCIPAL-NAME",
    ];

    [Theory]
    [InlineData("none")]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-signing-key")]
    [InlineData("alg-none")]
    [InlineData("hs256-signed-with-public-key")]
    public async Task Gateway_ProtectedRoute_InvalidOrMissingToken_Returns401AndForwardsNothing(string variant)
    {
        using var otherKeys = new TestKeyPair();
        var token = variant switch
        {
            "none" => null,
            "malformed" => "not.a.jwt",
            "expired" => TestTokens.Create(Keys.SigningKey, lifetime: TimeSpan.FromHours(-1)),
            "wrong-issuer" => TestTokens.Create(Keys.SigningKey, issuer: "someone-else"),
            "wrong-audience" => TestTokens.Create(Keys.SigningKey, audience: "another-api"),
            "wrong-signing-key" => TestTokens.Create(otherKeys.SigningKey),
            "alg-none" => TestTokens.CreateWithoutSignature(),
            "hs256-signed-with-public-key" => TestTokens.CreateHmacSignedWithKeyMaterial(Keys.PublicKeyPem),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };

        var response = await Client.SendAsync(Request(HttpMethod.Get, "/claims/123", token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await ReadType(response)).Should().Be("unauthenticated");
        Downstream.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/claims/123")]
    [InlineData("/dashboard/outstanding")]
    [InlineData("/staff/workload")]
    [InlineData("/notifications/mine")]
    public async Task Gateway_EveryProtectedPrefix_NoToken_Returns401(string path)
    {
        var response = await Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Downstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Gateway_ValidToken_ForwardsAuthorizationHeaderUnchanged()
    {
        var token = TestTokens.Create(Keys.SigningKey, roles: ["Officer", "Manager"]);

        var response = await Client.SendAsync(Request(HttpMethod.Get, "/claims/123", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var forwarded = Downstream.Requests.Should().ContainSingle().Subject;
        forwarded.Path.Should().Be("/claims/123");
        forwarded.Headers["Authorization"].Should().Be($"Bearer {token}");
    }

    [Fact]
    public async Task Gateway_SpoofedIdentityHeaders_AreNotForwarded()
    {
        var request = Request(HttpMethod.Get, "/claims/123", TestTokens.Create(Keys.SigningKey));
        request.Headers.Add("X-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Role", "Manager");
        foreach (var name in SpoofedHeaders)
        {
            request.Headers.Add(name, Guid.NewGuid().ToString());
        }

        await Client.SendAsync(request);

        var forwarded = Downstream.Requests.Should().ContainSingle().Subject;
        forwarded.Headers.Keys.Should().NotContain(SpoofedHeaders);
        forwarded.Headers.Should().ContainKey("Authorization");
    }

    [Fact]
    public async Task Gateway_ValidToken_WithSingleStringRole_IsForwarded()
    {
        var token = TestTokens.Create(Keys.SigningKey, singleRole: "Manager");

        var response = await Client.SendAsync(Request(HttpMethod.Get, "/claims/123", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Downstream.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Gateway_ServiceRoleRefusal_PassesThrough403Untouched()
    {
        var officer = TestTokens.Create(Keys.SigningKey, roles: ["Officer"]);
        var manager = TestTokens.Create(Keys.SigningKey, roles: ["Manager"]);

        var refused = await Client.SendAsync(Request(HttpMethod.Get, "/dashboard/outstanding", officer));
        var allowed = await Client.SendAsync(Request(HttpMethod.Get, "/dashboard/outstanding", manager));

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadType(refused)).Should().Be("role-not-allowed");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Gateway_AuthTokenRoute_IsAnonymousAndForwarded()
    {
        var response = await Client.PostAsync("/auth/token", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Downstream.Requests.Should().ContainSingle().Which.Path.Should().Be("/auth/token");
    }

    [Theory]
    [InlineData("POST", "/auth/refresh")]
    [InlineData("GET", "/auth/token")]
    [InlineData("POST", "/auth/token/extra")]
    public async Task Gateway_OtherAuthPaths_Return404(string method, string path)
    {
        var response = await Client.SendAsync(Request(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Downstream.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/internal/reconcile")]
    [InlineData("/dev/anything")]
    public async Task Gateway_InternalAndDevRoutes_Return404(string path)
    {
        var withToken = await Client.SendAsync(Request(HttpMethod.Get, path, TestTokens.Create(Keys.SigningKey)));
        var withoutToken = await Client.GetAsync(path);

        withToken.StatusCode.Should().Be(HttpStatusCode.NotFound);
        withoutToken.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Downstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Endpoints_AllRoutes_HaveNamedPolicyOrExplicitAnonymous()
    {
        EndpointPolicies.FindEndpointsWithoutExplicitPolicy(Factory.Services).Should().BeEmpty();
    }

    private static async Task<string?> ReadType(HttpResponseMessage response)
    {
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("type").GetString();
    }
}
