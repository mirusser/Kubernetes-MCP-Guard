using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InfraGate.ClientCredentials;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.ClientCredentials.Tests.UnitTests;

public sealed class ClientCredentialsTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_ReturnsCachedTokenOnSubsequentCall()
    {
        using var httpClient = CreateTokenHttpClient(out var handler);
        var options = ValidOptions();

        var provider = new ClientCredentialsTokenProvider(
            options,
            httpClient,
            TimeProvider.System,
            NullLogger<ClientCredentialsTokenProvider>.Instance);

        string first = await provider.GetTokenAsync(CancellationToken.None);
        string second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(first, second);
        // 2 HTTP calls on first acquire (discovery + token); second call is cached
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task RefreshTokenAsync_ForceRefreshes()
    {
        using var httpClient = CreateTokenHttpClient(out var handler);
        var options = ValidOptions();

        var provider = new ClientCredentialsTokenProvider(
            options,
            httpClient,
            TimeProvider.System,
            NullLogger<ClientCredentialsTokenProvider>.Instance);

        string first = await provider.GetTokenAsync(CancellationToken.None);
        string second = await provider.RefreshTokenAsync(CancellationToken.None);

        Assert.NotEqual(first, second);
        // First acquire: discovery (1) + token (2). Refresh: token (3) — no discovery, cached endpoint
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_ReturnsRawAccessTokenWithoutBearerPrefix()
    {
        using var httpClient = CreateTokenHttpClient(out _);
        var options = ValidOptions();

        var provider = new ClientCredentialsTokenProvider(
            options,
            httpClient,
            TimeProvider.System,
            NullLogger<ClientCredentialsTokenProvider>.Instance);

        string token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.DoesNotContain("Bearer ", token);
        // callCount=2 means second HTTP call (token endpoint) returns "my-access-token-2"
        Assert.StartsWith("my-access-token-", token);
    }

    private static HttpClient CreateTokenHttpClient(out FakeOidcHttpMessageHandler handler)
    {
        handler = new FakeOidcHttpMessageHandler();
        return new HttpClient(handler);
    }

    private static ClientCredentialsTokenOptions ValidOptions() => new()
    {
        Authority = "https://auth.example.com",
        ClientId = "my-client",
        ClientSecret = "my-secret",
        Scope = "mcp:tools.readonly"
    };

    private sealed class FakeOidcHttpMessageHandler : HttpMessageHandler
    {
        private int callCount;

        public int CallCount => callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref callCount);

            if (request.RequestUri!.AbsolutePath.Contains("openid-configuration"))
            {
                var discovery = JsonSerializer.Serialize(new
                {
                    token_endpoint = "https://auth.example.com/token"
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(discovery)
                });
            }

            var response = JsonSerializer.Serialize(new
            {
                access_token = $"my-access-token-{call}",
                expires_in = 3600
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response)
            });
        }
    }
}
