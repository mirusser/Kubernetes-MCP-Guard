using System.Net;
using System.Text;
using System.Text.Json;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway.DownstreamAuth;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpGateway.Tests.UnitTests.DownstreamAuth;

/// <summary>
/// Verifies that the token provider never writes the acquired token value or the
/// client secret to any log message, regardless of outcome.
/// </summary>
public sealed class ClientCredentialsTokenProviderRedactionTests
{
    private const string FakeAuthority = "http://localhost:9999/realms/test";
    private const string FakeClientId = "gateway-client";
    private const string FakeClientSecret = "super-secret-client-secret-value";
    private const string FakeScope = "mcp:downstream";
    private const string FakeAccessToken = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.sensitive-payload.signature";

    private static DownstreamAuthOptions CreateOptions() =>
        new()
        {
            Required = true,
            Authority = FakeAuthority,
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
        CapturingLogger<ClientCredentialsDownstreamServiceTokenProvider> logger)
    {
        var httpClient = new HttpClient(handler);
        return new ClientCredentialsDownstreamServiceTokenProvider(
            CreateOptions(),
            httpClient,
            new ManualTimeProvider(),
            logger);
    }

    [Fact]
    public async Task GetServiceTokenAsync_SuccessfulAcquisition_TokenValueNotInAnyLogMessage()
    {
        var logger = new CapturingLogger<ClientCredentialsDownstreamServiceTokenProvider>();
        var handler = new MockHttpMessageHandler(request =>
        {
            if (IsMetadataRequest(request))
            {
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            return OkJson(BuildTokenResponseJson(FakeAccessToken, 3600));
        });

        var provider = CreateProvider(handler, logger);
        await provider.GetServiceTokenAsync(CancellationToken.None);

        // The raw token or the bearer-prefixed token must not appear in any log message
        string rawToken = FakeAccessToken;
        string bearerToken = DownstreamAuthConventions.BearerPrefix + FakeAccessToken;

        foreach (string message in logger.Messages)
        {
            Assert.DoesNotContain(rawToken, message, StringComparison.Ordinal);
            Assert.DoesNotContain(bearerToken, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetServiceTokenAsync_TokenEndpointHttpError_ClientSecretNotInAnyLogMessage()
    {
        var logger = new CapturingLogger<ClientCredentialsDownstreamServiceTokenProvider>();
        var handler = new MockHttpMessageHandler(request =>
        {
            if (IsMetadataRequest(request))
            {
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("invalid_client: bad credentials")
            };
        });

        var provider = CreateProvider(handler, logger);

        // The token request will throw HttpRequestException; that is expected.
        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetServiceTokenAsync(CancellationToken.None));

        foreach (string message in logger.Messages)
        {
            Assert.DoesNotContain(FakeClientSecret, message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetServiceTokenAsync_TokenEndpointError_ExceptionMessageDoesNotContainTokenValue(
        HttpStatusCode statusCode)
    {
        var logger = new CapturingLogger<ClientCredentialsDownstreamServiceTokenProvider>();
        var handler = new MockHttpMessageHandler(request =>
        {
            if (IsMetadataRequest(request))
            {
                return OkJson(BuildOidcDiscoveryJson(TokenEndpoint()));
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent($"error: {FakeAccessToken}")
            };
        });

        var provider = CreateProvider(handler, logger);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => provider.GetServiceTokenAsync(CancellationToken.None));

        Assert.DoesNotContain(FakeClientSecret, ex.Message, StringComparison.Ordinal);
    }

    private static bool IsMetadataRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith(".well-known/openid-configuration", StringComparison.Ordinal);

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}
