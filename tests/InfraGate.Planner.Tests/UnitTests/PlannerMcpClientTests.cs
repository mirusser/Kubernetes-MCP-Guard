using InfraGate.ClientCredentials;
using InfraGate.Planner.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerMcpClientTests
{
    private static PlannerMcpClient CreateClient(string gatewayBaseUrl = "http://localhost:3001/mcp")
    {
        return CreateClient(gatewayBaseUrl, Substitute.For<IClientCredentialsTokenProvider>());
    }

    private static PlannerMcpClient CreateClient(
        string gatewayBaseUrl,
        IClientCredentialsTokenProvider tokenProvider)
    {
        var options = Substitute.For<IOptions<PlannerOptions>>();
        options.Value.Returns(new PlannerOptions { GatewayBaseUrl = gatewayBaseUrl });

        return new PlannerMcpClient(
            options,
            tokenProvider,
            NullLogger<PlannerMcpClient>.Instance,
            NullLoggerFactory.Instance);
    }

    [Fact]
    public void IsConnected_BeforeConnectAsync_ReturnsFalse()
    {
        var client = CreateClient();
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task CallToolAsync_NotConnected_ThrowsInvalidOperationException()
    {
        var client = CreateClient();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CallToolAsync(PlannerConventions.ToolNames.GetK8sStatus, null, CancellationToken.None));
    }

    [Theory]
    [InlineData("execute_approved_plan")]
    [InlineData("wait_for_plan_approval")]
    [InlineData("request_scale_deployment")]
    [InlineData("request_restart_deployment")]
    [InlineData("apply_manifest")]
    [InlineData("delete_resource")]
    [InlineData("set_image")]
    public async Task CallToolAsync_BlockedTool_ThrowsBeforeConnectionCheck(string toolName)
    {
        var tokenProvider = Substitute.For<IClientCredentialsTokenProvider>();
        var client = CreateClient("http://localhost:3001/mcp", tokenProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CallToolAsync(toolName, null, CancellationToken.None));

        await tokenProvider.DidNotReceive().GetTokenAsync(Arg.Any<CancellationToken>());
        await tokenProvider.DidNotReceive().RefreshTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CallToolAsync_WhitelistedToolNotConnected_ThrowsNotConnected()
    {
        var client = CreateClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CallToolAsync(PlannerConventions.ToolNames.GetK8sStatus, null, CancellationToken.None));
        Assert.Contains("not connected", ex.Message, StringComparison.OrdinalIgnoreCase);
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
