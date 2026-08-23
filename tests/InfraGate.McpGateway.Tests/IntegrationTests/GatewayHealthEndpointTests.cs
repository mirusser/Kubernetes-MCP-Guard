using System.Net;
using InfraGate.McpGateway.Endpoints;
using InfraGate.McpGateway.Tests.Fakes;
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
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync(McpGatewayConventions.Health.LivenessPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readyz_ReturnsServiceUnavailable_WhenPostgresUnreachable()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync(McpGatewayConventions.Health.ReadinessPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Readyz_UnreachablePostgresBody_ContainsNoRawExceptionText()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync(McpGatewayConventions.Health.ReadinessPath);
        string body = await response.Content.ReadAsStringAsync();

        // AC (c): no kubeconfig paths, tokens, arguments, or downstream payloads — in particular,
        // the raw connection-failure exception text must never reach the response body.
        Assert.DoesNotContain("Timeout", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readyz_SecondaryDegraded_ReportsSecondaryStatusWithoutFailingOverallReadiness()
    {
        var secondary = new FakeSupervisedDownstreamMcpClient { ProcessGeneration = 1 };
        var catalog = new DownstreamToolCatalog();
        catalog.RecordSourceDegraded(
            McpGatewayConventions.DownstreamSources.Secondary,
            McpGatewayMessages.ToolCatalog.RestartAttemptsExhausted);

        using var server = CreateServer(secondary: secondary, catalog: catalog);
        using var client = server.CreateClient();

        var response = await client.GetAsync(McpGatewayConventions.Health.ReadinessPath);
        string body = await response.Content.ReadAsStringAsync();

        // Postgres is still unreachable in this fixture, so overall readiness stays 503 — the
        // point of this test is that the secondary's own degraded status is surfaced distinctly
        // (AC (a)), not that it changes the mandatory-dependency status code.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Degraded", body, StringComparison.Ordinal);
    }

    private static TestServer CreateServer(
        IDownstreamMcpClient? primary = null,
        IDownstreamMcpClient? secondary = null,
        DownstreamToolCatalog? catalog = null) =>
        new(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1"));
                services.AddSingleton(catalog ?? new DownstreamToolCatalog());
                services.AddSingleton<IDownstreamMcpClient>(primary ?? new FakeSupervisedDownstreamMcpClient());
                services.AddSingleton(sp => new GatewayReadinessChecker(
                    sp.GetRequiredService<NpgsqlDataSource>(),
                    sp.GetRequiredService<IDownstreamMcpClient>(),
                    secondary,
                    sp.GetRequiredService<DownstreamToolCatalog>()));
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapGatewayHealthEndpoints());
            }));
}
