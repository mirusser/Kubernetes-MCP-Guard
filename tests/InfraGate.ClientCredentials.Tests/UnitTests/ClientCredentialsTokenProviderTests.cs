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
    public async Task GetTokenAsync_BeforeRefreshSkew_ReturnsCachedToken()
    {
        using var httpClient = CreateTokenHttpClient(out var handler, expiresInSeconds: 60);
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-24T12:00:00Z"));
        var options = ValidOptions() with { RefreshSkewSeconds = 30 };

        var provider = new ClientCredentialsTokenProvider(
            options,
            httpClient,
            timeProvider,
            NullLogger<ClientCredentialsTokenProvider>.Instance);

        string first = await provider.GetTokenAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(29));
        string second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, handler.TokenRequestCount);
    }

    [Fact]
    public async Task GetTokenAsync_InsideRefreshSkew_AcquiresFreshToken()
    {
        using var httpClient = CreateTokenHttpClient(out var handler, expiresInSeconds: 60);
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-24T12:00:00Z"));
        var options = ValidOptions() with { RefreshSkewSeconds = 30 };

        var provider = new ClientCredentialsTokenProvider(
            options,
            httpClient,
            timeProvider,
            NullLogger<ClientCredentialsTokenProvider>.Instance);

        string first = await provider.GetTokenAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        string second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.NotEqual(first, second);
        Assert.Equal(2, handler.TokenRequestCount);
    }

    [Fact]
    public async Task GetTokenAsync_ConcurrentAcquisition_RequestsOneToken()
    {
        using var httpClient = CreateTokenHttpClient(out var handler);
        var options = ValidOptions();

        var provider = new ClientCredentialsTokenProvider(
            options,
            httpClient,
            TimeProvider.System,
            NullLogger<ClientCredentialsTokenProvider>.Instance);

        var requests = Enumerable.Range(0, 12)
            .Select(_ => provider.GetTokenAsync(CancellationToken.None));

        var tokens = await Task.WhenAll(requests);

        Assert.All(tokens, token => Assert.Equal(tokens[0], token));
        Assert.Equal(1, handler.DiscoveryRequestCount);
        Assert.Equal(1, handler.TokenRequestCount);
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

    private static HttpClient CreateTokenHttpClient(
        out FakeOidcHttpMessageHandler handler,
        int expiresInSeconds = 3600)
    {
        handler = new FakeOidcHttpMessageHandler(expiresInSeconds);
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
        private readonly int expiresInSeconds;
        private int callCount;
        private int discoveryRequestCount;
        private int tokenRequestCount;

        public FakeOidcHttpMessageHandler(int expiresInSeconds)
        {
            this.expiresInSeconds = expiresInSeconds;
        }

        public int CallCount => callCount;
        public int DiscoveryRequestCount => discoveryRequestCount;
        public int TokenRequestCount => tokenRequestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref callCount);

            if (request.RequestUri!.AbsolutePath.Contains("openid-configuration"))
            {
                Interlocked.Increment(ref discoveryRequestCount);
                var discovery = JsonSerializer.Serialize(new
                {
                    token_endpoint = "https://auth.example.com/token"
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(discovery)
                });
            }

            Interlocked.Increment(ref tokenRequestCount);
            var response = JsonSerializer.Serialize(new
            {
                access_token = $"my-access-token-{call}",
                expires_in = expiresInSeconds
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response)
            });
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now;

        public ManualTimeProvider(DateTimeOffset initialNow)
        {
            now = initialNow;
        }

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta)
        {
            now += delta;
        }
    }
}
