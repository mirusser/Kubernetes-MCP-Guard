using System.Net;
using System.Text;
using System.Text.Json;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway.DownstreamAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpGateway.Tests.UnitTests.DownstreamAuth;

public sealed class ClientCredentialsDownstreamServiceTokenProviderTests
{
    private const string FakeAuthority = "http://localhost:9999/realms/test";
    private const string FakeClientId = "gateway-client";
    private const string FakeClientSecret = "secret";
    private const string FakeScope = "mcp:downstream";
    private const string FakeAccessToken = "eyJ.fake.token";

    private static DownstreamAuthOptions CreateOptions(string? metadataAddress = null) =>
        new()
        {
            Required = true,
            Authority = FakeAuthority,
            MetadataAddress = metadataAddress,
            RequireHttpsMetadata = false,
            GatewayClientId = FakeClientId,
            GatewayClientSecret = FakeClientSecret,
            Scope = FakeScope,
        };

    private static string TokenEndpoint() => $"{FakeAuthority}/protocol/openid-connect/token";

    private static string BuildOidcDiscoveryJson(string tokenEndpoint) =>
        JsonSerializer.Serialize(new { token_endpoint = tokenEndpoint });

    private static string BuildTokenResponseJson(string accessToken, int expiresIn) =>
        JsonSerializer.Serialize(new { access_token = accessToken, expires_in = expiresIn, token_type = "Bearer" });

    private static ClientCredentialsDownstreamServiceTokenProvider CreateProvider(
        MockHttpMessageHandler handler,
        DownstreamAuthOptions? options = null,
        ManualTimeProvider? timeProvider = null)
    {
        var httpClient = new HttpClient(handler);
        var opts = options ?? CreateOptions();
        var time = timeProvider ?? new ManualTimeProvider();

        return new ClientCredentialsDownstreamServiceTokenProvider(
            opts,
            httpClient,
            time,
            NullLogger<ClientCredentialsDownstreamServiceTokenProvider>.Instance);
    }

    [Fact]
    public async Task GetServiceTokenAsync_CacheHit_ReturnsSameTokenWithoutCallingEndpoint()
    {
        int callCount = 0;
        var handler = new MockHttpMessageHandler(request =>
        {
            callCount++;
            if (IsMetadataRequest(request))
            {
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            return OkJson(BuildTokenResponseJson(FakeAccessToken, 3600));
        });

        var provider = CreateProvider(handler);

        string first = await provider.GetServiceTokenAsync(CancellationToken.None);
        string second = await provider.GetServiceTokenAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(DownstreamAuthConventions.BearerPrefix + FakeAccessToken, first);
        // Discovery + token = 2 calls on first acquire; second call must not hit endpoint
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetServiceTokenAsync_TokenWithin60sOfExpiry_TriggersRefresh()
    {
        var timeProvider = new ManualTimeProvider();
        int tokenCallCount = 0;
        var handler = new MockHttpMessageHandler(request =>
        {
            if (IsMetadataRequest(request))
            {
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            tokenCallCount++;
            string token = tokenCallCount == 1 ? "first-token" : "refreshed-token";
            return OkJson(BuildTokenResponseJson(token, 3600));
        });

        var provider = CreateProvider(handler, timeProvider: timeProvider);

        string first = await provider.GetServiceTokenAsync(CancellationToken.None);
        Assert.Equal(DownstreamAuthConventions.BearerPrefix + "first-token", first);

        // Advance time so token is within the 60s skew window (expires_in=3600, advance 3541s)
        timeProvider.Advance(TimeSpan.FromSeconds(3541));

        string second = await provider.GetServiceTokenAsync(CancellationToken.None);
        Assert.Equal(DownstreamAuthConventions.BearerPrefix + "refreshed-token", second);
        Assert.Equal(2, tokenCallCount);
    }

    [Fact]
    public async Task GetServiceTokenAsync_TokenMoreThan60sFromExpiry_DoesNotRefresh()
    {
        var timeProvider = new ManualTimeProvider();
        int tokenCallCount = 0;
        var handler = new MockHttpMessageHandler(request =>
        {
            if (IsMetadataRequest(request))
            {
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            tokenCallCount++;
            return OkJson(BuildTokenResponseJson(FakeAccessToken, 3600));
        });

        var provider = CreateProvider(handler, timeProvider: timeProvider);

        await provider.GetServiceTokenAsync(CancellationToken.None);

        // Advance time but stay more than 60s from expiry (advance 3000s, 600s remain, skew=60s)
        timeProvider.Advance(TimeSpan.FromSeconds(3000));

        await provider.GetServiceTokenAsync(CancellationToken.None);

        Assert.Equal(1, tokenCallCount);
    }

    [Fact]
    public async Task RefreshServiceTokenAsync_BypassesCache_AcquiresNewToken()
    {
        int tokenCallCount = 0;
        var handler = new MockHttpMessageHandler(request =>
        {
            if (IsMetadataRequest(request))
            {
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            tokenCallCount++;
            string token = tokenCallCount == 1 ? "cached-token" : "force-refreshed-token";
            return OkJson(BuildTokenResponseJson(token, 3600));
        });

        var provider = CreateProvider(handler);

        string cached = await provider.GetServiceTokenAsync(CancellationToken.None);
        Assert.Equal(DownstreamAuthConventions.BearerPrefix + "cached-token", cached);

        string refreshed = await provider.RefreshServiceTokenAsync(CancellationToken.None);
        Assert.Equal(DownstreamAuthConventions.BearerPrefix + "force-refreshed-token", refreshed);
        Assert.Equal(2, tokenCallCount);
    }

    [Fact]
    public async Task GetServiceTokenAsync_TokenEndpointFailure_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler(request =>
        {
            if (IsMetadataRequest(request))
            {
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Service unavailable")
            };
        });

        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetServiceTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetServiceTokenAsync_MissingAccessTokenInResponse_ThrowsInvalidOperationException()
    {
        var handler = new MockHttpMessageHandler(request =>
        {
            if (IsMetadataRequest(request))
            {
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            // Response without access_token field
            return OkJson(JsonSerializer.Serialize(new { expires_in = 3600, token_type = "Bearer" }));
        });

        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetServiceTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetServiceTokenAsync_ConcurrentCacheMiss_EndpointCalledExactlyOnce()
    {
        var metadataGate = new SemaphoreSlim(0, 1);
        var tokenGate = new SemaphoreSlim(0, 1);
        int tokenCallCount = 0;

        var handler = new MockHttpMessageHandler(async request =>
        {
            if (IsMetadataRequest(request))
            {
                await metadataGate.WaitAsync();
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            Interlocked.Increment(ref tokenCallCount);
            await tokenGate.WaitAsync();
            return OkJson(BuildTokenResponseJson(FakeAccessToken, 3600));
        });

        var provider = CreateProvider(handler);

        var task1 = Task.Run(() => provider.GetServiceTokenAsync(CancellationToken.None));
        var task2 = Task.Run(() => provider.GetServiceTokenAsync(CancellationToken.None));

        // Let both tasks start, then release both gates
        await Task.Delay(50);
        metadataGate.Release();
        tokenGate.Release();

        string[] results = await Task.WhenAll(task1, task2);

        Assert.All(results, r => Assert.Equal(DownstreamAuthConventions.BearerPrefix + FakeAccessToken, r));
        // Single-flight: only one token request despite two concurrent callers
        Assert.Equal(1, tokenCallCount);
    }

    [Fact]
    public async Task GetServiceTokenAsync_WithExplicitMetadataAddress_UsesConfiguredAddress()
    {
        string customMetadataAddress = "http://localhost:9999/custom-metadata";
        string customTokenEndpoint = "http://localhost:9999/custom-token";
        bool usedCustomMetadata = false;

        var handler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith(customMetadataAddress, StringComparison.Ordinal))
            {
                usedCustomMetadata = true;
                return OkJson(BuildOidcDiscoveryJson(customTokenEndpoint));
            }

            if (request.RequestUri!.AbsoluteUri.StartsWith(customTokenEndpoint, StringComparison.Ordinal))
            {
                return OkJson(BuildTokenResponseJson(FakeAccessToken, 3600));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var options = CreateOptions(metadataAddress: customMetadataAddress);
        var provider = CreateProvider(handler, options: options);

        string token = await provider.GetServiceTokenAsync(CancellationToken.None);

        Assert.True(usedCustomMetadata);
        Assert.Equal(DownstreamAuthConventions.BearerPrefix + FakeAccessToken, token);
    }

    [Fact]
    public async Task GetServiceTokenAsync_ClientCredentialsRequest_UsesCorrectGrantTypeAndScope()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(async request =>
        {
            if (IsMetadataRequest(request))
            {
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            capturedBody = await request.Content!.ReadAsStringAsync();
            return OkJson(BuildTokenResponseJson(FakeAccessToken, 3600));
        });

        var provider = CreateProvider(handler);

        await provider.GetServiceTokenAsync(CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("grant_type=client_credentials", capturedBody, StringComparison.Ordinal);
        Assert.Contains($"client_id={FakeClientId}", capturedBody, StringComparison.Ordinal);
        Assert.Contains($"scope={Uri.EscapeDataString(FakeScope)}", capturedBody, StringComparison.Ordinal);
    }

    private static bool IsMetadataRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith(".well-known/openid-configuration", StringComparison.Ordinal);

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset current = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => current;

    public void Advance(TimeSpan amount) => current = current.Add(amount);
}

internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        this.handler = request => Task.FromResult(handler(request));
    }

    public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        this.handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        handler(request);
}
