using System.Net;
using System.Net.Http.Headers;
using System.Text;
using InfraGate.McpGateway.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class HttpTokenIntrospectionClientTests
{
    private const string Authority = "https://issuer.example.com";
    private const string Endpoint = "https://issuer.example.com/protocol/openid-connect/token/introspect";
    private const string ClientId = "gateway-resource-server";
    private const string ClientSecret = "secret-placeholder";

    [Fact]
    public async Task IntrospectAsync_ActiveResponse_ReturnsActiveResult()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(Endpoint, request.RequestUri?.ToString());
            AssertBasicAuthentication(request.Headers.Authorization);
            return JsonResponse($"{{\"{GatewayAuthConventions.TokenIntrospection.ActiveResponsePropertyName}\":true,\"exp\":{expiresAt.ToUnixTimeSeconds()}}}");
        });
        var client = CreateClient(handler, endpoint: Endpoint);
        var token = CreateToken(expiresAt);

        var result = await client.IntrospectAsync(token, CancellationToken.None);

        Assert.True(result.IsActive);
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), result.ExpiresAt?.ToUnixTimeSeconds());
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData($"{{\"{GatewayAuthConventions.TokenIntrospection.ActiveResponsePropertyName}\":false}}", HttpStatusCode.OK)]
    [InlineData($"{{\"{GatewayAuthConventions.TokenIntrospection.ActiveResponsePropertyName}\":\"true\"}}", HttpStatusCode.OK)]
    [InlineData($"{{\"{GatewayAuthConventions.TokenIntrospection.ActiveResponsePropertyName}\":true,\"exp\":9223372036854775807}}", HttpStatusCode.OK)]
    [InlineData("not-json", HttpStatusCode.OK)]
    [InlineData($"{{\"{GatewayAuthConventions.TokenIntrospection.ActiveResponsePropertyName}\":true}}", HttpStatusCode.InternalServerError)]
    public async Task IntrospectAsync_InactiveMalformedOrHttpFailure_ReturnsInactive(
        string responseBody,
        HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler, endpoint: Endpoint);
        var token = CreateToken(DateTimeOffset.UtcNow.AddMinutes(5));

        var result = await client.IntrospectAsync(token, CancellationToken.None);

        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task IntrospectAsync_EndpointOmitted_UsesDiscoveryIntrospectionEndpoint()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal(Authority + GatewayAuthConventions.TokenIntrospection.OpenIdConnectDiscoveryPath, request.RequestUri?.ToString());
                return JsonResponse($"{{\"{GatewayAuthConventions.TokenIntrospection.IntrospectionEndpointMetadataName}\":\"{Endpoint}\"}}");
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(Endpoint, request.RequestUri?.ToString());
            return JsonResponse($"{{\"{GatewayAuthConventions.TokenIntrospection.ActiveResponsePropertyName}\":true}}");
        });
        var client = CreateClient(handler, endpoint: null);
        var token = CreateToken(DateTimeOffset.UtcNow.AddMinutes(5));

        var result = await client.IntrospectAsync(token, CancellationToken.None);

        Assert.True(result.IsActive);
        Assert.Equal(2, handler.CallCount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData($"{{\"{GatewayAuthConventions.TokenIntrospection.IntrospectionEndpointMetadataName}\":42}}")]
    public async Task IntrospectAsync_DiscoveryDoesNotExposeEndpoint_ReturnsInactive(string discoveryResponse)
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(discoveryResponse));
        var client = CreateClient(handler, endpoint: null);
        var token = CreateToken(DateTimeOffset.UtcNow.AddMinutes(5));

        var result = await client.IntrospectAsync(token, CancellationToken.None);

        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task IntrospectAsync_RequestTimeout_ReturnsInactive()
    {
        var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("timeout"));
        var client = CreateClient(handler, endpoint: Endpoint);
        var token = CreateToken(DateTimeOffset.UtcNow.AddMinutes(5));

        var result = await client.IntrospectAsync(token, CancellationToken.None);

        Assert.False(result.IsActive);
    }

    private static HttpTokenIntrospectionClient CreateClient(StubHttpMessageHandler handler, string? endpoint)
    {
        var options = new GatewayAuthOptions(
            Authority,
            TokenIntrospectionEndpoint: endpoint,
            TokenIntrospectionClientId: ClientId,
            TokenIntrospectionClientSecret: ClientSecret);
        return new HttpTokenIntrospectionClient(new HttpClient(handler), options);
    }

    private static JsonWebToken CreateToken(DateTimeOffset expiresAt)
    {
        var key = new SymmetricSecurityKey("0123456789abcdef0123456789abcdef"u8.ToArray());
        var issuedAt = expiresAt.AddMinutes(-4);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = GatewayAuthConventions.DefaultOAuthResource,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebToken(new JsonWebTokenHandler().CreateToken(descriptor));
    }

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private static void AssertBasicAuthentication(AuthenticationHeaderValue? authorization)
    {
        Assert.NotNull(authorization);
        Assert.Equal(GatewayAuthConventions.TokenIntrospection.BasicAuthenticationScheme, authorization!.Scheme);
        string expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
        Assert.Equal(expected, authorization.Parameter);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(handler(request));
        }
    }
}
