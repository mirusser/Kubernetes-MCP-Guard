using InfraGate.McpGateway;
using Microsoft.AspNetCore.Http;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class SmokeTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task GatewayMcpEndpoint_WithoutBearer_ReturnsUnauthorized()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        using var client = fixture.CreateGatewayHttpClient();

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(StatusCodes.Status401Unauthorized, (int)response.StatusCode);
    }

    [Fact]
    public async Task GatewayMcpEndpoint_WithValidBearer_DoesNotReturn401Or403()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        string token = await fixture.AcquireTokenAsync();
        using var client = fixture.CreateGatewayHttpClient(token);

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.NotEqual(StatusCodes.Status401Unauthorized, (int)response.StatusCode);
        Assert.NotEqual(StatusCodes.Status403Forbidden, (int)response.StatusCode);
    }
}
