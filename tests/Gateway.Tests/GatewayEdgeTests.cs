using System.Net;
using System.Text.Json;
using Chubb.Contracts;
using Chubb.TestSupport;

namespace Chubb.Gateway.Tests;

public sealed class GatewayEdgeTests : GatewayTestBase
{
    [Theory]
    [InlineData("/claims/123", "GET")]
    [InlineData("/auth/token", "POST")]
    public async Task Gateway_Preflight_AllowedOrigin_ReturnsCorsHeaders(string path, string method)
    {
        var response = await Client.SendAsync(Preflight(path, method, GatewayFactory.AllowedOrigin));

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(GatewayFactory.AllowedOrigin);
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        response.Headers.GetValues("Access-Control-Allow-Headers").Single().Should().Contain("Authorization");
        Downstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Gateway_Preflight_AllowsConcurrencyAndIdempotencyHeaders()
    {
        var request = Preflight("/claims/123/decision", "PUT", GatewayFactory.AllowedOrigin);
        request.Headers.Remove("Access-Control-Request-Headers");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,if-match,idempotency-key");

        var response = await Client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Headers").Single().ToLowerInvariant()
            .Should().Contain("if-match").And.Contain("idempotency-key");
        Downstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Gateway_Response_ExposesEtagAndReplayHeadersToTheFrontend()
    {
        var request = Request(HttpMethod.Get, "/claims/1", TestTokens.Create(Keys.SigningKey));
        request.Headers.Add("Origin", GatewayFactory.AllowedOrigin);

        var response = await Client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Expose-Headers").Single()
            .Should().Contain("ETag").And.Contain("Idempotent-Replayed").And.Contain("Retry-After").And.Contain("Location")
            .And.Contain(HeaderNames.CorrelationId);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Gateway_PayeeDetails_Authenticated_Returns501AndNeverReachesClaims(string method)
    {
        var request = Request(new HttpMethod(method), "/claims/123/payee-details", TestTokens.Create(Keys.SigningKey));

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        (await ProblemType(response)).Should().Be("not-implemented");
        Downstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Gateway_PayeeDetails_WithoutToken_Returns401AndNeverReachesClaims()
    {
        var response = await Client.GetAsync("/claims/123/payee-details");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Downstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Gateway_Preflight_OtherOrigin_ReturnsNoCorsHeaders()
    {
        var response = await Client.SendAsync(Preflight("/claims/123", "GET", "https://evil.example"));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Gateway_Request_EchoesOrGeneratesCorrelationIdAndForwardsIt()
    {
        var token = TestTokens.Create(Keys.SigningKey);
        var inbound = Guid.NewGuid().ToString();
        var withInbound = Request(HttpMethod.Get, "/claims/1", token);
        withInbound.Headers.Add(HeaderNames.CorrelationId, inbound);
        var withInvalid = Request(HttpMethod.Get, "/claims/2", token);
        withInvalid.Headers.Add(HeaderNames.CorrelationId, "not-a-guid");

        var echoed = await Client.SendAsync(withInbound);
        var generated = await Client.SendAsync(withInvalid);

        echoed.Headers.GetValues(HeaderNames.CorrelationId).Should().Equal(inbound);
        var newId = generated.Headers.GetValues(HeaderNames.CorrelationId).Single();
        Guid.TryParse(newId, out _).Should().BeTrue();
        newId.Should().NotBe("not-a-guid");
        Downstream.Requests.Select(request => request.Headers[HeaderNames.CorrelationId])
            .Should().BeEquivalentTo(inbound, newId);
    }

    [Fact]
    public async Task Gateway_OversizedBody_Returns413ProblemDetails()
    {
        var response = await Client.PostAsync("/auth/token", new StringContent(new string('x', 5000)));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        Downstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Gateway_ProxiedBodyOverOneMegabyte_Returns413()
    {
        var request = Request(HttpMethod.Post, "/claims", TestTokens.Create(Keys.SigningKey));
        request.Content = new StringContent(new string('x', 1_048_577));

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        Downstream.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("*")]
    [InlineData("localhost:4200")]
    [InlineData("http://localhost:4200/app")]
    public void Gateway_InvalidCorsOrigin_FailsAtStartup(string origin)
    {
        using var factory = new GatewayFactory("http://127.0.0.1:1", Keys, allowedOrigin: origin);

        var start = () => factory.CreateClient();

        start.Should().Throw<Exception>();
    }

    [Fact]
    public async Task Gateway_UnmappedRouteAndUpstreamFailure_ReturnProblemDetailsWithoutInternals()
    {
        await using var factory = new GatewayFactory("http://127.0.0.1:1", Keys);
        using var client = factory.CreateClient();
        var token = TestTokens.Create(Keys.SigningKey);
        using var upstreamDown = new HttpRequestMessage(HttpMethod.Get, "/claims/1");
        upstreamDown.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

        var unmapped = await client.GetAsync("/nope");
        var failed = await client.SendAsync(upstreamDown);

        (await ProblemType(unmapped)).Should().Be("not-found");
        failed.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await failed.Content.ReadAsStringAsync();
        (await ProblemType(failed)).Should().Be("upstream-unavailable");
        body.Should().NotContain("Exception").And.NotContain("127.0.0.1").And.NotContain(" at ");
    }

    [Fact]
    public async Task Gateway_Health_IsAnonymous()
    {
        (await Client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpRequestMessage Preflight(string path, string method, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    private static async Task<string?> ProblemType(HttpResponseMessage response)
    {
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("type").GetString();
    }
}
