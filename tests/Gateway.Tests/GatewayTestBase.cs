using Chubb.TestSupport;

namespace Chubb.Gateway.Tests;

public abstract class GatewayTestBase : IAsyncLifetime
{
    protected TestKeyPair Keys { get; } = new();

    protected StubDownstream Downstream { get; private set; } = null!;

    protected GatewayFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected virtual string Environment => "Development";

    protected virtual string? ContractsPath => null;

    public async Task InitializeAsync()
    {
        Downstream = await StubDownstream.StartAsync();
        Factory = new GatewayFactory(Downstream.Address, Keys, Environment, ContractsPath);
        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await Downstream.DisposeAsync();
        Keys.Dispose();
        CleanUp();
    }

    protected virtual void CleanUp()
    {
    }

    protected HttpRequestMessage Request(HttpMethod method, string path, string? token = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        return request;
    }
}
