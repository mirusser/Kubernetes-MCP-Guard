using System.Net;
using InfraGate.McpGateway.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// ASPDEPR004/ASPDEPR008: see suppression rationale in GatewayHttpMcpIntegrationTests.cs.
#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

namespace InfraGate.McpGateway.Tests.IntegrationTests;

public sealed class GatewayHealthEndpointTests
{
    [Fact]
    public async Task Healthz_AlwaysReturnsHealthy()
    {
        using var server = CreateServer(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1"));
        using var client = server.CreateClient();

        var response = await client.GetAsync(McpGatewayConventions.Health.LivenessPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readyz_ReturnsServiceUnavailable_WhenPostgresUnreachable()
    {
        using var server = CreateServer(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1"));
        using var client = server.CreateClient();

        var response = await client.GetAsync(McpGatewayConventions.Health.ReadinessPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static TestServer CreateServer(NpgsqlDataSource dataSource) =>
        new(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(dataSource);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapGatewayHealthEndpoints());
            }));
}
