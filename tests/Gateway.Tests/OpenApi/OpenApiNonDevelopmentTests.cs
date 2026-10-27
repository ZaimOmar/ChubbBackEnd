using System.Net;

namespace Chubb.Gateway.Tests.OpenApi;

public sealed class OpenApiNonDevelopmentTests : GatewayTestBase
{
    protected override string Environment => "Production";

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger")]
    public async Task OpenApi_NonDevelopment_EndpointAbsent(string path)
    {
        (await Client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
