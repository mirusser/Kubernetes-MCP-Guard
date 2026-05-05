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

namespace InfraGate.McpGateway.Tests.UnitTests;

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
    public async Task McpEndpoint_JwtWithoutRequiredScope_ReturnsStepUpAuthorizationChallenge()
    {
        using var server = CreateOAuthServer(out var signingKey);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            GatewayAuthConventions.AuthorizationScheme,
            CreateJwt(signingKey, scope: "other:scope"));

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        if (client.BaseAddress is null)
        {
            throw new InvalidOperationException("Test client base address is required.");
        }

        var expectedResourceMetadata = new Uri(
            client.BaseAddress,
            GatewayAuthConventions.Metadata.ProtectedResourcePath).ToString();
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal(GatewayAuthConventions.AuthorizationScheme, challenge.Scheme);
        Assert.Contains(
            $"{GatewayAuthConventions.ChallengeParameters.Error}=\"{GatewayAuthConventions.OAuthErrors.InsufficientScope}\"",
            challenge.Parameter);
        Assert.Contains(
            $"{GatewayAuthConventions.ChallengeParameters.Scope}=\"{Scope}\"",
            challenge.Parameter);
        Assert.Contains(
            $"{GatewayAuthConventions.ChallengeParameters.ResourceMetadata}=\"{expectedResourceMetadata}\"",
            challenge.Parameter);
    }

    [Fact]
    public void AddGatewayAuthentication_UsesMetadataAddressOverride_WithPublicIssuerValidation()
    {
        const string metadataAddress = "http://devissuer:3011/.well-known/openid-configuration";
        var options = new GatewayAuthOptions(
            Issuer,
            Resource,
            Scope,
            OAuthRequireHttpsMetadata: false,
            OAuthMetadataAddress: metadataAddress);
        var services = new ServiceCollection();

        services.AddGatewayAuthentication(options);
        using var provider = services.BuildServiceProvider();
        var jwtOptions = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(Issuer, jwtOptions.Authority);
        Assert.Equal(metadataAddress, jwtOptions.MetadataAddress);
        Assert.Contains(Issuer, jwtOptions.TokenValidationParameters.ValidIssuers);
        Assert.Contains(Issuer.TrimEnd('/'), jwtOptions.TokenValidationParameters.ValidIssuers);
    }

    private static TestServer CreateOAuthServer() =>
        CreateOAuthServer(out _);

    private static TestServer CreateOAuthServer(out SecurityKey signingKey)
    {
        var key = new SymmetricSecurityKey("0123456789abcdef0123456789abcdef"u8.ToArray())
        {
            KeyId = "test-key"
        };
        signingKey = key;
        var options = new GatewayAuthOptions(
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
