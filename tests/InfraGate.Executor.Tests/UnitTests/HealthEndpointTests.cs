// ASPDEPR004: WebHostBuilder is deprecated in favor of WebApplicationBuilder.
// ASPDEPR008: TestServer(IWebHostBuilder) is deprecated — use IServiceProvider ctor.
// Suppressed because: WebApplicationFactory<T> requires InternalsVisibleTo
// and a public Program class, which is overkill for these endpoint tests.
#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008
using System.Text.Json;
using InfraGate.ClientCredentials;
using InfraGate.Executor.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task GetHealth_TokenNotYetAcquired_ReturnsStarting()
    {
        var tokenProvider = Substitute.For<IClientCredentialsTokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(string.Empty);

        using var server = CreateServer(tokenProvider);
        using var client = server.CreateClient();
        using var response = await client.GetAsync(ExecutorConventions.HealthEndpointPath);

        Assert.Equal(503, (int)response.StatusCode);

        using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        Assert.Equal("starting", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetHealth_TokenAcquired_ReturnsHealthy()
    {
        var tokenProvider = Substitute.For<IClientCredentialsTokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("valid-token");

        using var server = CreateServer(tokenProvider);
        using var client = server.CreateClient();
        using var response = await client.GetAsync(ExecutorConventions.HealthEndpointPath);

        Assert.Equal(200, (int)response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_TokenAcquisitionThrows_ReturnsUnhealthy()
    {
        var tokenProvider = Substitute.For<IClientCredentialsTokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new InvalidOperationException("auth failed")));

        using var server = CreateServer(tokenProvider);
        using var client = server.CreateClient();
        using var response = await client.GetAsync(ExecutorConventions.HealthEndpointPath);

        Assert.Equal(503, (int)response.StatusCode);

        using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        Assert.Equal("unhealthy", document.RootElement.GetProperty("status").GetString());
    }

    private static TestServer CreateServer(IClientCredentialsTokenProvider tokenProvider)
    {
        return new TestServer(new WebHostBuilder()
            .UseEnvironment(Environments.Development)
            .ConfigureServices(services =>
            {
                services.AddSingleton(tokenProvider);
                services.AddRouting();
                services.AddLogging();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapExecutorHealthEndpoint();
                });
            }));
    }
}
