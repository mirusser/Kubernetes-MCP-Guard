using InfraGate.ClientCredentials;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverMcpClientTests
{
    private static ObserverMcpClient CreateClient(string gatewayBaseUrl = "http://localhost:3001/mcp")
    {
        var options = Substitute.For<IOptions<ObserverOptions>>();
        options.Value.Returns(new ObserverOptions { GatewayBaseUrl = gatewayBaseUrl });

        var tokenProvider = Substitute.For<IClientCredentialsTokenProvider>();
        var logger = NullLogger<ObserverMcpClient>.Instance;
        var loggerFactory = NullLoggerFactory.Instance;

        return new ObserverMcpClient(options, tokenProvider, logger, loggerFactory);
    }

    [Fact]
    public void IsConnected_BeforeConnectAsync_ReturnsFalse()
    {
        var client = CreateClient();
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task GetToolResultAsync_NotConnected_ThrowsInvalidOperationException()
    {
        var client = CreateClient();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetToolResultAsync("get_k8s_status", null, CancellationToken.None));
    }

    [Fact]
    public async Task GetToolResultAsync_NonWhitelistedTool_ThrowsBeforeConnectionCheck()
    {
        var client = CreateClient();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetToolResultAsync("request_scale_deployment", null, CancellationToken.None));
    }

    [Fact]
    public async Task GetToolResultAsync_WhitelistedTool_ThrowsNotConnected()
    {
        var client = CreateClient();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetToolResultAsync("get_k8s_status", null, CancellationToken.None));
    }

    [Fact]
    public async Task DisposeAsync_NotConnected_DoesNotThrow()
    {
        var client = CreateClient();
        await client.DisposeAsync();
    }

    [Fact]
    public void GatewayBaseUrl_ReflectsOptions()
    {
        var client = CreateClient("http://localhost:3001/mcp");
        Assert.Equal("http://localhost:3001/mcp", client.GatewayBaseUrl);
    }
}
