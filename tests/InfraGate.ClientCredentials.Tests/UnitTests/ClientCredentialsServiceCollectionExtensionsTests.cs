using InfraGate.ClientCredentials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfraGate.ClientCredentials.Tests.UnitTests;

public sealed class ClientCredentialsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddClientCredentialsTokenProvider_RegistersProviderAndHttpClient()
    {
        var services = new ServiceCollection();
        var options = new ClientCredentialsTokenOptions
        {
            Authority = "https://auth.example.com",
            ClientId = "my-client",
            ClientSecret = "my-secret",
            Scope = "mcp:tools.readonly"
        };

        services.AddLogging();
        services.AddHttpClient();
        services.AddClientCredentialsTokenProvider(options);

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IClientCredentialsTokenProvider>();

        Assert.NotNull(provider);
        Assert.IsType<ClientCredentialsTokenProvider>(provider);
    }

    [Fact]
    public void AddClientCredentialsTokenProvider_WithoutPreRegisteredHttpClient_RegistersProvider()
    {
        var services = new ServiceCollection();
        var options = new ClientCredentialsTokenOptions
        {
            Authority = "https://auth.example.com",
            ClientId = "my-client",
            ClientSecret = "my-secret",
            Scope = "mcp:tools.readonly"
        };

        services.AddLogging();
        services.AddClientCredentialsTokenProvider(options);

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IClientCredentialsTokenProvider>();

        Assert.NotNull(provider);
        Assert.IsType<ClientCredentialsTokenProvider>(provider);
    }

    [Fact]
    public void AddClientCredentialsBearerHandler_RegistersDelegatingHandler()
    {
        var services = new ServiceCollection();
        var options = new ClientCredentialsTokenOptions
        {
            Authority = "https://auth.example.com",
            ClientId = "my-client",
            ClientSecret = "my-secret",
            Scope = "mcp:tools.readonly"
        };

        services.AddLogging();
        services.AddHttpClient();
        services.AddClientCredentialsTokenProvider(options);
        services.AddClientCredentialsBearerHandler();

        var sp = services.BuildServiceProvider();
        var handler = sp.GetRequiredService<ClientCredentialsBearerHandler>();

        Assert.NotNull(handler);
    }

    [Fact]
    public void AddClientCredentialsTokenProvider_MissingApiKey_FailsFast()
    {
        var services = new ServiceCollection();
        var options = new ClientCredentialsTokenOptions
        {
            Authority = "https://auth.example.com",
            ClientId = string.Empty,
            Scope = "mcp:tools.readonly"
        };

        services.AddLogging();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddClientCredentialsTokenProvider(options));

        Assert.Contains("ClientId", exception.Message);
    }

    [Fact]
    public void AddClientCredentialsBearerHandler_WithHttpClient_AppliesHandler()
    {
        var services = new ServiceCollection();
        var options = new ClientCredentialsTokenOptions
        {
            Authority = "https://auth.example.com",
            ClientId = "my-client",
            ClientSecret = "my-secret",
            Scope = "mcp:tools.readonly"
        };

        services.AddLogging();
        services.AddHttpClient();
        services.AddClientCredentialsTokenProvider(options);
        services.AddHttpClient("test-client")
            .AddClientCredentialsBearerHandler();

        var sp = services.BuildServiceProvider();
        var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var client = clientFactory.CreateClient("test-client");

        Assert.NotNull(client);
    }

    [Fact]
    public void AddClientCredentialsTokenProvider_MissingAuthority_FailsFast()
    {
        var services = new ServiceCollection();
        var options = new ClientCredentialsTokenOptions
        {
            Authority = string.Empty,
            ClientId = "my-client",
            Scope = "mcp:tools.readonly"
        };

        services.AddLogging();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddClientCredentialsTokenProvider(options));

        Assert.Contains("Authority", exception.Message);
    }
}
