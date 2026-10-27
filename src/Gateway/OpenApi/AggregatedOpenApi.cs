using Microsoft.Extensions.Options;

namespace Chubb.Gateway.OpenApi;

public sealed class AggregatedOpenApi(IOptions<OpenApiOptions> options, IHostEnvironment environment)
{
    public string Json { get; } = OpenApiAggregator.BuildJson(options.Value.ResolveContractsPath(environment));
}
