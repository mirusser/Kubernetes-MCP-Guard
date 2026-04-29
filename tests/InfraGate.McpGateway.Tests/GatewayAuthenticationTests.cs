using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

namespace InfraGate.McpGateway.Tests;

public sealed class GatewayAuthenticationTests
{
    private const string Issuer = "https://issuer.example.com";
    private const string Resource = "http://127.0.0.1:3001/mcp";
    private const string Scope = "mcp:tools";

    [Fact]
    public async Task McpEndpoint_ReturnsOAuthDiscoveryChallenge_WhenTokenIsMissing()
    {
        using var server = CreateOAuthServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task ProtectedResourceMetadata_IsPublicAndContainsOAuthSettings()
    {
        using var server = CreateOAuthServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync(GatewayAuthConventions.Metadata.ProtectedResourcePath);

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        Assert.Equal(Resource, root.GetProperty("resource").GetString());
        Assert.Equal(Issuer, root.GetProperty("authorization_servers")[0].GetString());
        Assert.Equal(Scope, root.GetProperty("scopes_supported")[0].GetString());
    }

    [Fact]
    public async Task McpEndpoint_AllowsStaticBearerToken_WhenOAuthIsEnabled()
    {
        using var server = CreateOAuthServer(staticBearerToken: "secret");
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        response.EnsureSuccessStatusCode();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task McpEndpoint_RejectsWrongStaticBearerToken_WhenOAuthIsEnabled()
    {
        using var server = CreateOAuthServer(staticBearerToken: "secret");
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_AllowsStaticBearerToken_WhenOAuthIsDisabled()
    {
        using var server = CreateStaticBearerServer("secret");
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        response.EnsureSuccessStatusCode();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task McpEndpoint_RejectsWrongStaticBearerToken_WhenOAuthIsDisabled()
    {
        using var server = CreateStaticBearerServer("secret");
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task McpEndpoint_AllowsValidJwt()
    {
        using var server = CreateOAuthServer(out var signingKey);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(signingKey));

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        response.EnsureSuccessStatusCode();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    public async Task McpEndpoint_RejectsInvalidJwt(string invalidCase)
    {
        using var server = CreateOAuthServer(out var signingKey);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            invalidCase switch
            {
                "wrong-issuer" => CreateJwt(signingKey, issuer: "https://other.example.com"),
                "wrong-audience" => CreateJwt(signingKey, audience: "https://other.example.com/mcp"),
                "expired" => CreateJwt(signingKey, expires: DateTime.UtcNow.AddMinutes(-5)),
                _ => throw new InvalidOperationException()
            });

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_RejectsMalformedJwt()
    {
        using var server = CreateOAuthServer();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_ForbidsJwtWithoutRequiredScope()
    {
        using var server = CreateOAuthServer(out var signingKey);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(signingKey, scope: "other:scope"));

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static TestServer CreateOAuthServer() =>
        CreateOAuthServer(out _);

    private static TestServer CreateOAuthServer(string? staticBearerToken) =>
        CreateOAuthServer(out _, staticBearerToken);

    private static TestServer CreateOAuthServer(out SecurityKey signingKey, string? staticBearerToken = null)
    {
        var key = new SymmetricSecurityKey("0123456789abcdef0123456789abcdef"u8.ToArray())
        {
            KeyId = "test-key"
        };
        signingKey = key;
        var options = new GatewayAuthOptions(
            staticBearerToken,
            Issuer,
            Resource,
            Scope,
            OAuthRequireHttpsMetadata: true);

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(options);
                services.AddGatewayAuthentication(options);
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwtOptions =>
                {
                    jwtOptions.Configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = Issuer
                    };
                    jwtOptions.Configuration.SigningKeys.Add(key);
                    jwtOptions.TokenValidationParameters.IssuerSigningKey = key;
                    jwtOptions.TokenValidationParameters.ValidIssuer = Issuer;
                    jwtOptions.TokenValidationParameters.ValidAudience = Resource;
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

    private static TestServer CreateStaticBearerServer(string staticBearerToken)
    {
        var options = new GatewayAuthOptions(staticBearerToken);

        return CreateServer(options, configureJwt: null);
    }

    private static TestServer CreateServer(
        GatewayAuthOptions options,
        Action<IServiceCollection>? configureJwt)
    {
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(options);
                services.AddGatewayAuthentication(options);
                configureJwt?.Invoke(services);
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

    private static string CreateJwt(
        SecurityKey signingKey,
        string issuer = Issuer,
        string audience = Resource,
        string scope = Scope,
        DateTime? expires = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            Claims = new Dictionary<string, object>
            {
                [GatewayAuthConventions.Claims.Subject] = "subject-1",
                [GatewayAuthConventions.Claims.Scope] = scope
            },
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
