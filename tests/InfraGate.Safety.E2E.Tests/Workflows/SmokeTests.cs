using System.Net;
using InfraGate.McpGateway;
using ModelContextProtocol.Client;

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

        using var httpClient = fixture.CreateGatewayHttpClient();
        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, McpGatewayConventions.McpPath),
                Name = "infra-gate-safety-e2e-unauthenticated",
                TransportMode = HttpTransportMode.StreamableHttp
            },
            httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await using var unexpectedClient = await McpClient.CreateAsync(
                transport,
                cancellationToken: CancellationToken.None);
        });

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task GatewayMcpEndpoint_WithValidBearer_DoesNotReturn401Or403()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        await using var client = await fixture.CreateHttpMcpClientAsync(CancellationToken.None);

        Assert.NotEmpty(client.Subject);
    }
}
