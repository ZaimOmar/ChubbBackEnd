using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Chubb.TestSupport;

public static class EndpointPolicies
{
    public static IReadOnlyList<string> FindEndpointsWithoutExplicitPolicy(IServiceProvider services)
    {
        var dataSource = services.GetRequiredService<EndpointDataSource>();
        return dataSource.Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is null
                && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(endpoint => endpoint.DisplayName ?? "(unnamed endpoint)")
            .ToList();
    }
}
