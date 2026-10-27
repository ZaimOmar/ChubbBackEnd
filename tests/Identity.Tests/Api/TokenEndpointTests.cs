using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chubb.Contracts;
using Chubb.TestSupport;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Chubb.Identity.Tests.Api;

[Collection(PostgresCollection.Name)]
public sealed class TokenEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly TestKeyPair _keys = new();
    private IdentityApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new IdentityApiFactory(await postgres.CreateDatabaseAsync(), _keys);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _keys.Dispose();
    }

    [Fact]
    public async Task PostToken_ValidCredentials_ReturnsBearerToken()
    {
        var response = await Login("officer.manager@chubb.test", IdentityApiFactory.SeedPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains(HeaderNames.CorrelationId).Should().BeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tokenType").GetString().Should().Be("Bearer");
        body.GetProperty("expiresIn").GetInt32().Should().Be(1800);
        var jwt = new JsonWebToken(body.GetProperty("accessToken").GetString());
        jwt.Alg.Should().Be("RS256");
        jwt.Subject.Should().Be(DevSeed.SeedUserIds.OfficerManager.ToString());
        jwt.GetPayloadValue<string[]>(ClaimNames.Role).Should().BeEquivalentTo(Roles.Officer, Roles.Manager);
    }

    [Fact]
    public async Task PostToken_WrongPasswordOrUnknownEmail_Returns401WithIdenticalBody()
    {
        var wrongPassword = await Login("officer@chubb.test", "wrong-password");
        var unknownEmail = await Login("nobody@chubb.test", "wrong-password");

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var wrongPasswordProblem = await ReadProblem(wrongPassword);
        var unknownEmailProblem = await ReadProblem(unknownEmail);
        wrongPasswordProblem.Type.Should().Be("invalid-credentials");
        unknownEmailProblem.Should().BeEquivalentTo(wrongPasswordProblem);
        _factory.VerifierSpy.Calls.Should().Equal(false, true);
    }

    [Fact]
    public async Task PostToken_InvalidBody_Returns400ProblemDetails()
    {
        var response = await Login("not-an-email", "");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Type.Should().Be("validation-error");
    }

    [Fact]
    public async Task PostToken_MalformedJson_Returns400ProblemDetails()
    {
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/auth/token", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Type.Should().Be("bad-request");
    }

    [Fact]
    public async Task PostToken_MalformedJson_InProduction_Returns400ProblemDetailsWithoutErrorLog()
    {
        await using var production = new IdentityApiFactory(await postgres.CreateDatabaseAsync(), _keys, "Production");
        using var client = production.CreateClient();
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/auth/token", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).Type.Should().Be("bad-request");
        production.Logs.Entries.Should().NotContain(entry => entry.Contains("BadHttpRequestException"));
    }

    [Fact]
    public async Task PostToken_MalformedJson_DoesNotLogStackTrace()
    {
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        await _client.PostAsync("/auth/token", content);

        _factory.Logs.Entries.Should().NotContain(entry => entry.Contains("BadHttpRequestException"));
    }

    [Fact]
    public async Task Unmapped_Route_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync("/nope");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblem(response)).Type.Should().Be("not-found");
    }

    [Fact]
    public async Task PostToken_OversizedBody_Returns413ProblemDetails()
    {
        using var content = new StringContent(new string('x', 5000), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/auth/token", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await ReadProblem(response)).Type.Should().Be("payload-too-large");
    }

    [Fact]
    public async Task PostToken_InboundCorrelationId_IsEchoed()
    {
        var correlationId = Guid.NewGuid().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/token")
        {
            Content = JsonContent.Create(new { email = "x", password = "" }),
        };
        request.Headers.Add(HeaderNames.CorrelationId, correlationId);

        var response = await _client.SendAsync(request);

        response.Headers.GetValues(HeaderNames.CorrelationId).Should().Equal(correlationId);
    }

    [Fact]
    public async Task Login_Failure_LogsContainNoEmailPasswordOrToken()
    {
        await Login("victim@chubb.test", "Sup3r-Secret-Pw");
        var success = await Login("officer@chubb.test", IdentityApiFactory.SeedPassword);
        var token = (await success.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var logs = string.Join('\n', _factory.Logs.Entries);

        logs.Should().NotBeEmpty();
        logs.Should().NotContain("victim@chubb.test").And.NotContain("Sup3r-Secret-Pw")
            .And.NotContain(token).And.NotContain(IdentityApiFactory.SeedPassword);
    }

    [Fact]
    public void Endpoints_AllRoutes_HaveNamedPolicyOrExplicitAnonymous()
    {
        EndpointPolicies.FindEndpointsWithoutExplicitPolicy(_factory.Services).Should().BeEmpty();
    }

    [Fact]
    public async Task Health_LiveAndReady_Return200Anonymously()
    {
        (await _client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> Login(string email, string password) =>
        _client.PostAsJsonAsync("/auth/token", new { email, password });

    private static async Task<ProblemBody> ReadProblem(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new ProblemBody(json.GetProperty("type").GetString(), json.GetProperty("title").GetString());
    }

    private sealed record ProblemBody(string? Type, string? Title);
}
