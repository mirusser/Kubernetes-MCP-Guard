using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using InfraGate.ClientCredentials;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace InfraGate.ClientCredentials.Tests.UnitTests;

public sealed class ClientCredentialsBearerHandlerTests
{
    [Fact]
    public async Task SendAsync_InjectsBearerHeader()
    {
        var tokenProvider = new Mock<IClientCredentialsTokenProvider>();
        tokenProvider.Setup(tp => tp.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("my-access-token");

        using var innerHandler = new FakeInnerHandler(HttpStatusCode.OK);
        using var handler = new ClientCredentialsBearerHandler(
            tokenProvider.Object,
            NullLogger<ClientCredentialsBearerHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var httpClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        using var response = await httpClient.SendAsync(request);

        Assert.NotNull(innerHandler.LastRequest);
        Assert.Equal("Bearer", innerHandler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("my-access-token", innerHandler.LastRequest!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task SendAsync_WithDpopProvider_InjectsDpopAuthorizationAndProofHeader()
    {
        var tokenProvider = new StubDpopTokenProvider("my-access-token", "my-dpop-proof");
        using var innerHandler = new FakeInnerHandler(HttpStatusCode.OK);
        using var handler = new ClientCredentialsBearerHandler(
            tokenProvider,
            NullLogger<ClientCredentialsBearerHandler>.Instance)
        {
            InnerHandler = innerHandler
        };
        var httpClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api?ignored=true");

        using var response = await httpClient.SendAsync(request);

        Assert.NotNull(innerHandler.LastRequest);
        Assert.Equal("DPoP", innerHandler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("my-access-token", innerHandler.LastRequest!.Headers.Authorization!.Parameter);
        var proof = Assert.Single(innerHandler.LastRequest.Headers.GetValues("DPoP"));
        Assert.Equal("my-dpop-proof", proof);
        Assert.Equal(HttpMethod.Get, tokenProvider.LastRequest!.Method);
        Assert.Equal(
            "http://example.com/api",
            tokenProvider.LastRequest.RequestUri!.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task SendAsync_On401_RefreshesTokenAndRetriesOnce()
    {
        var tokenProvider = new Mock<IClientCredentialsTokenProvider>();
        tokenProvider.Setup(tp => tp.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("stale-token");
        tokenProvider.Setup(tp => tp.RefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("refreshed-token");

        using var innerHandler = new FakeInnerHandler(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.OK);
        using var handler = new ClientCredentialsBearerHandler(
            tokenProvider.Object,
            NullLogger<ClientCredentialsBearerHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var httpClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        using var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        tokenProvider.Verify(tp => tp.RefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, innerHandler.CallCount);
        Assert.Equal("refreshed-token", innerHandler.LastRequest!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task SendAsync_OnTwo401s_ReturnsUnauthorized()
    {
        var tokenProvider = new Mock<IClientCredentialsTokenProvider>();
        tokenProvider.Setup(tp => tp.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("stale-token");
        tokenProvider.Setup(tp => tp.RefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("still-stale");

        using var innerHandler = new FakeInnerHandler(HttpStatusCode.Unauthorized);
        using var handler = new ClientCredentialsBearerHandler(
            tokenProvider.Object,
            NullLogger<ClientCredentialsBearerHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var httpClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        using var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        tokenProvider.Verify(tp => tp.RefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class FakeInnerHandler : DelegatingHandler
    {
        private readonly HttpStatusCode[] statusCodes;
        private int callIndex;

        public int CallCount => callIndex;
        public HttpRequestMessage? LastRequest { get; private set; }

        public FakeInnerHandler(params HttpStatusCode[] statusCodes)
        {
            this.statusCodes = statusCodes;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var status = callIndex < statusCodes.Length
                ? statusCodes[callIndex]
                : statusCodes[^1];
            Interlocked.Increment(ref callIndex);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class StubDpopTokenProvider(string token, string proof) :
        IClientCredentialsTokenProvider,
        IClientCredentialsDpopProofProvider
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public bool IsDPoPEnabled => true;

        public Task<string> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);

        public Task<string> RefreshTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);

        public Task<string> CreateDpopProofAsync(
            string accessToken,
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(proof);
        }
    }
}
