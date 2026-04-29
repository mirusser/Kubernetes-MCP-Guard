using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InfraGate.DevIssuer;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008
namespace InfraGate.DevIssuer.Tests;

public sealed class DevIssuerTests
{
    private const string Issuer = "http://127.0.0.1:3011";
    private const string Resource = "http://127.0.0.1:3001/mcp";
    private const string Scope = "mcp:tools";
    private const string Subject = "test-dev-user";
    private const string RedirectUri = "http://127.0.0.1:4567/callback";
    private const string CodeVerifier = "test-code-verifier-with-enough-entropy";

    [Fact]
    public async Task DiscoveryAndJwks_ReturnUsableIssuerMetadata()
    {
        using var server = CreateIssuerServer();
        using var client = server.CreateClient();

        var metadata = await GetJsonAsync(client, DevIssuerConventions.Endpoints.AuthorizationServerMetadata);
        var jwks = await GetJsonAsync(client, DevIssuerConventions.Endpoints.Jwks);

        Assert.Equal(Issuer, metadata.GetProperty(DevIssuerConventions.Json.Issuer).GetString());
        Assert.Equal(
            Issuer + DevIssuerConventions.Endpoints.Authorize,
            metadata.GetProperty(DevIssuerConventions.Json.AuthorizationEndpoint).GetString());
        Assert.Equal(
            Issuer + DevIssuerConventions.Endpoints.Token,
            metadata.GetProperty(DevIssuerConventions.Json.TokenEndpoint).GetString());
        Assert.Equal(
            Issuer + DevIssuerConventions.Endpoints.Register,
            metadata.GetProperty(DevIssuerConventions.Json.RegistrationEndpoint).GetString());
        Assert.Equal(Scope, metadata.GetProperty(DevIssuerConventions.Json.ScopesSupported)[0].GetString());
        Assert.Equal(DevIssuerConventions.OAuth.RsaKeyType, jwks.GetProperty(DevIssuerConventions.Json.Keys)[0].GetProperty(DevIssuerConventions.Json.JsonWebKeyType).GetString());
        Assert.False(string.IsNullOrWhiteSpace(jwks.GetProperty(DevIssuerConventions.Json.Keys)[0].GetProperty(DevIssuerConventions.Json.KeyId).GetString()));
    }

    [Fact]
    public async Task AuthorizationCodeFlow_ReturnsGatewayAcceptedJwt()
    {
        using var issuerServer = CreateIssuerServer();
        using var issuerClient = issuerServer.CreateClient();
        var clientId = await RegisterClientAsync(issuerClient);
        var code = await AuthorizeAsync(issuerClient, clientId);
        var accessToken = await RequestTokenAsync(issuerClient, clientId, code);

        using var gatewayServer = CreateGatewayServer(issuerServer);
        using var gatewayClient = gatewayServer.CreateClient();
        gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            GatewayAuthConventions.AuthorizationScheme,
            accessToken);

        var response = await gatewayClient.GetAsync(McpGatewayConventions.McpPath);

        response.EnsureSuccessStatusCode();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Token_RejectsReusedAuthorizationCode()
    {
        using var server = CreateIssuerServer();
        using var client = server.CreateClient();
        var clientId = await RegisterClientAsync(client);
        var code = await AuthorizeAsync(client, clientId);

        _ = await RequestTokenAsync(client, clientId, code);
        var response = await PostTokenAsync(client, clientId, code, CodeVerifier, Resource);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertOAuthErrorAsync(response, DevIssuerConventions.Errors.InvalidGrant);
    }

    [Fact]
    public async Task Token_RejectsWrongPkceVerifier()
    {
        using var server = CreateIssuerServer();
        using var client = server.CreateClient();
        var clientId = await RegisterClientAsync(client);
        var code = await AuthorizeAsync(client, clientId);

        var response = await PostTokenAsync(client, clientId, code, "wrong-verifier", Resource);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertOAuthErrorAsync(response, DevIssuerConventions.Errors.InvalidGrant);
    }

    [Fact]
    public async Task Token_RejectsWrongResource()
    {
        using var server = CreateIssuerServer();
        using var client = server.CreateClient();
        var clientId = await RegisterClientAsync(client);
        var code = await AuthorizeAsync(client, clientId);

        var response = await PostTokenAsync(client, clientId, code, CodeVerifier, "http://127.0.0.1:3001/other");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertOAuthErrorAsync(response, DevIssuerConventions.Errors.InvalidGrant);
    }

    [Fact]
    public async Task Authorize_RejectsMissingRequiredScope()
    {
        using var server = CreateIssuerServer();
        using var client = server.CreateClient();
        var clientId = await RegisterClientAsync(client);

        var response = await client.GetAsync(AuthorizeUrl(clientId, "other:scope"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertOAuthErrorAsync(response, DevIssuerConventions.Errors.InvalidScope);
    }

    [Fact]
    public async Task Register_RejectsNonLoopbackRedirectUri()
    {
        using var server = CreateIssuerServer();
        using var client = server.CreateClient();
        var request = new Dictionary<string, object?>
        {
            [DevIssuerConventions.Json.RedirectUris] = new[] { "https://example.com/callback" }
        };

        var response = await client.PostAsJsonAsync(DevIssuerConventions.Endpoints.Register, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertOAuthErrorAsync(response, DevIssuerConventions.Errors.InvalidRequest);
    }

    private static TestServer CreateIssuerServer()
    {
        var options = new DevIssuerOptions(Issuer, Resource, Scope, Subject);

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddDevIssuer(options);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapDevIssuer());
            }));
    }

    private static TestServer CreateGatewayServer(TestServer issuerServer)
    {
        var options = new GatewayAuthOptions(
            BearerToken: null,
            OAuthAuthority: Issuer,
            OAuthResource: Resource,
            OAuthScope: Scope,
            OAuthRequireHttpsMetadata: false);

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddGatewayAuthentication(options);
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwtOptions =>
                {
                    jwtOptions.Backchannel = issuerServer.CreateClient();
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet(McpGatewayConventions.McpPath, () => "ok")
                        .RequireAuthorization(GatewayAuthConventions.Schemes.PolicyName);
                });
            }));
    }

    private static async Task<string> RegisterClientAsync(HttpClient client)
    {
        var request = new Dictionary<string, object?>
        {
            [DevIssuerConventions.Json.RedirectUris] = new[] { RedirectUri },
            [DevIssuerConventions.Json.ClientName] = "Codex test"
        };
        var response = await client.PostAsJsonAsync(DevIssuerConventions.Endpoints.Register, request);

        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);

        return json.GetProperty(DevIssuerConventions.Json.ClientId).GetString()!;
    }

    private static async Task<string> AuthorizeAsync(HttpClient client, string clientId)
    {
        var response = await client.GetAsync(AuthorizeUrl(clientId, Scope));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(RedirectUri, response.Headers.Location!.GetLeftPart(UriPartial.Path));

        var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
        Assert.Equal("state-1", query[DevIssuerConventions.Parameters.State].ToString());

        return query[DevIssuerConventions.Parameters.Code].ToString();
    }

    private static string AuthorizeUrl(string clientId, string scope)
    {
        var query = new Dictionary<string, string?>
        {
            [DevIssuerConventions.Parameters.ResponseType] = DevIssuerConventions.OAuth.CodeResponseType,
            [DevIssuerConventions.Parameters.ClientId] = clientId,
            [DevIssuerConventions.Parameters.RedirectUri] = RedirectUri,
            [DevIssuerConventions.Parameters.CodeChallenge] = CodeChallenge(CodeVerifier),
            [DevIssuerConventions.Parameters.CodeChallengeMethod] = DevIssuerConventions.OAuth.S256CodeChallengeMethod,
            [DevIssuerConventions.Parameters.Resource] = Resource,
            [DevIssuerConventions.Parameters.Scope] = scope,
            [DevIssuerConventions.Parameters.State] = "state-1"
        };

        return QueryHelpers.AddQueryString(DevIssuerConventions.Endpoints.Authorize, query);
    }

    private static async Task<string> RequestTokenAsync(HttpClient client, string clientId, string code)
    {
        var response = await PostTokenAsync(client, clientId, code, CodeVerifier, Resource);

        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);

        Assert.Equal(DevIssuerConventions.OAuth.BearerTokenType, json.GetProperty(DevIssuerConventions.Json.TokenType).GetString());
        Assert.Equal(Scope, json.GetProperty(DevIssuerConventions.Json.Scope).GetString());

        return json.GetProperty(DevIssuerConventions.Json.AccessToken).GetString()!;
    }

    private static Task<HttpResponseMessage> PostTokenAsync(
        HttpClient client,
        string clientId,
        string code,
        string codeVerifier,
        string resource)
    {
        return client.PostAsync(
            DevIssuerConventions.Endpoints.Token,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [DevIssuerConventions.Parameters.GrantType] = DevIssuerConventions.OAuth.AuthorizationCodeGrantType,
                [DevIssuerConventions.Parameters.Code] = code,
                [DevIssuerConventions.Parameters.RedirectUri] = RedirectUri,
                [DevIssuerConventions.Parameters.ClientId] = clientId,
                [DevIssuerConventions.Parameters.CodeVerifier] = codeVerifier,
                [DevIssuerConventions.Parameters.Resource] = resource
            }));
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();

        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);

        return document.RootElement.Clone();
    }

    private static async Task AssertOAuthErrorAsync(HttpResponseMessage response, string expectedError)
    {
        var json = await ReadJsonAsync(response);

        Assert.Equal(expectedError, json.GetProperty(DevIssuerConventions.Json.Error).GetString());
    }

    private static string CodeChallenge(string codeVerifier)
    {
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));

        return Base64UrlEncode(challengeBytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
