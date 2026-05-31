using InfraGate.ClientCredentials;
using InfraGate.Executor.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorMcpClientTests
{
    [Fact]
    public void IsConnected_BeforeConnect_ReturnsFalse()
    {
        var client = CreateClient();

        Assert.False(client.IsConnected);
    }

    [Fact]
    public void GatewayBaseUrl_ReflectsOptionsValue()
    {
        var client = CreateClient("http://gateway:3001/mcp");

        Assert.Equal("http://gateway:3001/mcp", client.GatewayBaseUrl);
    }

    [Fact]
    public async Task CallToolAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        var client = CreateClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CallToolAsync(
                ExecutorConventions.ToolNames.WaitForPlanApproval,
                null,
                CancellationToken.None));
    }

    [Fact]
    public async Task CallToolAsync_DisallowedTool_ThrowsInvalidOperationException()
    {
        var client = CreateClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CallToolAsync("delete_all", null, CancellationToken.None));
    }

    [Fact]
    public void CreateHttpClient_ReturnsClientWithExpectedBaseAddress()
    {
        var tokenProvider = new StubTokenProvider();
        var loggerFactory = NullLoggerFactory.Instance;

        using var httpClient = ExecutorMcpClient.CreateHttpClient(
            "http://gateway.test:3001",
            tokenProvider,
            loggerFactory,
            new FakeHttpMessageHandler());

        Assert.Equal(new Uri("http://gateway.test:3001"), httpClient.BaseAddress);
    }

    [Fact]
    public async Task DisposeAsync_WhenNotConnected_DoesNotThrow()
    {
        var client = CreateClient();

        var ex = await Record.ExceptionAsync(() => client.DisposeAsync().AsTask());
        Assert.Null(ex);
    }

    private static ExecutorMcpClient CreateClient(string gatewayBaseUrl = "http://gateway:3001")
    {
        var options = Options.Create(new ExecutorOptions { GatewayBaseUrl = gatewayBaseUrl });
        return new ExecutorMcpClient(options, new StubTokenProvider(), NullLoggerFactory.Instance);
    }

    private sealed class StubTokenProvider : IClientCredentialsTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult("stub-token");

        public Task<string> RefreshTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult("stub-token");
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
