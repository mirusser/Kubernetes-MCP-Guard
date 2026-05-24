using InfraGate.ClientCredentials;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverMcpClientTests
{
    private static ObserverMcpClient CreateClient(string gatewayBaseUrl = "http://localhost:3001/mcp")
    {
        return CreateClient(gatewayBaseUrl, Substitute.For<IClientCredentialsTokenProvider>());
    }

    private static ObserverMcpClient CreateClient(
        string gatewayBaseUrl,
        IClientCredentialsTokenProvider tokenProvider)
    {
        var options = Substitute.For<IOptions<ObserverOptions>>();
        options.Value.Returns(new ObserverOptions { GatewayBaseUrl = gatewayBaseUrl });

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

    [Theory]
    [InlineData("request_scale_deployment")]
    [InlineData("execute_approved_plan")]
    [InlineData("apply_manifest")]
    [InlineData("delete_resource")]
    [InlineData("restart_deployment")]
    [InlineData("set_image")]
    public async Task GetToolResultAsync_MutationTool_RejectsBeforeTokenAcquisition(string toolName)
    {
        var tokenProvider = Substitute.For<IClientCredentialsTokenProvider>();
        var client = CreateClient("http://localhost:3001/mcp", tokenProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetToolResultAsync(toolName, null, CancellationToken.None));

        await tokenProvider.DidNotReceive().GetTokenAsync(Arg.Any<CancellationToken>());
        await tokenProvider.DidNotReceive().RefreshTokenAsync(Arg.Any<CancellationToken>());
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
        var ex = await Record.ExceptionAsync(() => client.DisposeAsync().AsTask());
        Assert.Null(ex);
    }

    [Fact]
    public void GatewayBaseUrl_ReflectsOptions()
    {
        var client = CreateClient("http://localhost:3001/mcp");
        Assert.Equal("http://localhost:3001/mcp", client.GatewayBaseUrl);
    }
}
