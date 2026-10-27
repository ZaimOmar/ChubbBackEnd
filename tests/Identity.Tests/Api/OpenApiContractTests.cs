using System.Text.Json.Nodes;
using Chubb.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Readers;
using Swashbuckle.AspNetCore.Swagger;

namespace Chubb.Identity.Tests.Api;

[Collection(PostgresCollection.Name)]
public sealed class OpenApiContractTests(PostgresFixture postgres)
{
    [Fact]
    public async Task IdentityOpenApi_RegeneratedSpec_MatchesCommittedContract()
    {
        using var keys = new TestKeyPair();
        await using var factory = new IdentityApiFactory(await postgres.CreateDatabaseAsync(), keys);
        var generated = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        var committedYaml = await File.ReadAllTextAsync(Path.Combine(RepositoryPaths.ContractsDirectory, "identity.yaml"));
        var committed = new OpenApiStringReader().Read(committedYaml, out var diagnostic);

        diagnostic.Errors.Should().BeEmpty();
        var generatedJson = JsonNode.Parse(generated.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0));
        var committedJson = JsonNode.Parse(committed.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0));
        JsonNode.DeepEquals(generatedJson, committedJson).Should().BeTrue(
            "contracts/openapi/identity.yaml must equal the running code; generated: {0}", generatedJson!.ToJsonString());
    }
}
