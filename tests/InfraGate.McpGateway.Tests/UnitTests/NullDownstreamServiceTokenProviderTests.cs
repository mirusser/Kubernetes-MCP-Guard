using InfraGate.McpGateway.DownstreamAuth;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class NullDownstreamServiceTokenProviderTests
{
    private readonly NullDownstreamServiceTokenProvider provider = new();

    [Fact]
    public async Task GetServiceTokenAsync_ReturnsEmptyString()
    {
        var result = await provider.GetServiceTokenAsync(CancellationToken.None);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task RefreshServiceTokenAsync_ReturnsEmptyString()
    {
        var result = await provider.RefreshServiceTokenAsync(CancellationToken.None);

        Assert.Equal(string.Empty, result);
    }
}
