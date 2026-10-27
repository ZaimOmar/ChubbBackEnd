using System.Net;
using System.Text.Json.Nodes;
using Chubb.TestSupport;

namespace Chubb.Gateway.Tests.OpenApi;

public sealed class OpenApiAggregationTests : IAsyncLifetime
{
    private readonly ContractDirectory _contracts = new();
    private readonly TestKeyPair _keys = new();
    private StubDownstream _downstream = null!;

    public async Task InitializeAsync() => _downstream = await StubDownstream.StartAsync();

    public async Task DisposeAsync()
    {
        await _downstream.DisposeAsync();
        _keys.Dispose();
        _contracts.Dispose();
    }

    [Fact]
    public async Task OpenApi_Aggregate_MergesCommittedFilesAndRemovesInternalPaths()
    {
        _contracts.Copy(Path.Combine(RepositoryPaths.ContractsDirectory, "identity.yaml"));
        _contracts.Write("claims.yaml", "/claims/{id}", "ClaimsClaimSummary", extraPaths: ["/internal/reconcile", "/health/live"]);
        using var client = CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var paths = document["paths"]!.AsObject().Select(pair => pair.Key).ToList();
        paths.Should().BeEquivalentTo("/auth/token", "/claims/{id}");
        document["components"]!["schemas"]!.AsObject().Select(pair => pair.Key)
            .Should().Contain(["IdentityLoginRequest", "ClaimsClaimSummary"]);
        document["components"]!["securitySchemes"]!["bearer"]!["scheme"]!.GetValue<string>().Should().Be("bearer");
        document["paths"]!["/auth/token"]!["post"]!["security"].Should().BeNull();
        document["paths"]!["/claims/{id}"]!["get"]!["security"]!.AsArray().Should().HaveCount(1);
    }

    [Fact]
    public async Task OpenApi_Aggregate_UsesCommittedFilesWithoutDownstreamCalls()
    {
        _contracts.Write("claims.yaml", "/claims/{id}", "ClaimsClaimSummary");
        using var client = CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _downstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenApi_Development_EndpointServed()
    {
        _contracts.Write("claims.yaml", "/claims/{id}", "ClaimsClaimSummary");
        using var client = CreateClient();

        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/swagger/index.html")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void OpenApi_InvalidContract_FailsAtStartup()
    {
        _contracts.Write("claims.yaml", "/claims/{id}", "ClaimsClaimSummary", extraComponents: "  responses:\n    Shared:\n      description: x");

        var start = CreateClient;

        start.Should().Throw<Exception>().WithMessage("*unsupported component kinds*");
    }

    [Fact]
    public void OpenApi_MissingContractsDirectory_FailsAtStartup()
    {
        var missing = Path.Combine(_contracts.Path, "missing");
        using var factory = new GatewayFactory(_downstream.Address, _keys, contractsPath: missing);

        var start = () => factory.CreateClient();

        start.Should().Throw<Exception>().WithMessage("*ContractsPath*");
    }

    private HttpClient CreateClient() =>
        new GatewayFactory(_downstream.Address, _keys, contractsPath: _contracts.Path).CreateClient();
}
