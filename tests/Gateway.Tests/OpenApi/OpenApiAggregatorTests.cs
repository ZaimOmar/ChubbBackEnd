using Chubb.Gateway.OpenApi;

namespace Chubb.Gateway.Tests.OpenApi;

public sealed class OpenApiAggregatorTests : IDisposable
{
    private readonly ContractDirectory _contracts = new();

    public void Dispose() => _contracts.Dispose();

    [Fact]
    public void OpenApi_Aggregate_DuplicateSchemaNames_Fails()
    {
        _contracts.Write("claims.yaml", "/claims/{id}", "SharedName");
        _contracts.Write("reporting.yaml", "/dashboard/outstanding", "SharedName");

        var build = () => OpenApiAggregator.Build(_contracts.Path);

        build.Should().Throw<OpenApiMergeException>().WithMessage("*SharedName*");
    }

    [Fact]
    public void OpenApi_Aggregate_DuplicatePaths_Fails()
    {
        _contracts.Write("claims.yaml", "/claims/{id}", "ClaimsSummary");
        _contracts.Write("other.yaml", "/claims/{id}", "OtherSummary");

        var build = () => OpenApiAggregator.Build(_contracts.Path);

        build.Should().Throw<OpenApiMergeException>().WithMessage("*/claims/{id}*");
    }

    [Fact]
    public void OpenApi_Aggregate_ComponentKindsOtherThanSchemas_Fails()
    {
        _contracts.Write(
            "claims.yaml", "/claims/{id}", "ClaimsSummary", extraComponents: "  parameters:\n    Page:\n      name: page\n      in: query\n      schema:\n        type: integer");

        var build = () => OpenApiAggregator.Build(_contracts.Path);

        build.Should().Throw<OpenApiMergeException>().WithMessage("*parameters*");
    }

    [Fact]
    public void OpenApi_Aggregate_ExcludesWholeSegmentsOnly()
    {
        _contracts.Write(
            "claims.yaml", "/claims/{id}", "ClaimsSummary",
            extraPaths: ["/internal/reconcile", "/health/ready", "/healthcheck-report", "/internalnotes"]);

        var document = OpenApiAggregator.Build(_contracts.Path);

        document.Paths.Keys.Should().BeEquivalentTo("/claims/{id}", "/healthcheck-report", "/internalnotes");
    }
}
