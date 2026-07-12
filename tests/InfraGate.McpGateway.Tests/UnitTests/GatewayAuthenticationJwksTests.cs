using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

// ASPDEPR004/ASPDEPR008: WebHostBuilder + TestServer are deprecated in favor of WebApplicationBuilder.
// Suppressed because: WebApplicationFactory<T> requires a public Program class — overkill for isolated auth tests.
#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

namespace InfraGate.McpGateway.Tests.UnitTests;

/// <summary>
/// Verifies that the gateway JWT bearer pipeline enforces strict kid matching when the
/// signing keys come from a ConfigurationManager (the production path).
/// </summary>
public sealed class GatewayAuthenticationJwksTests : IDisposable
{
    private const string Issuer = "https://issuer.example.com";
    private const string Resource = "http://127.0.0.1:3001/mcp";
    private const string Scope = "mcp:tools";

    private readonly List<RSA> rsaKeys = [];

    public void Dispose()
    {
        foreach (var rsa in rsaKeys)
        {
            rsa.Dispose();
        }
    }

    [Fact]
    public async Task McpEndpoint_ValidKid_AllowsRequest()
    {
        using var server = CreateServer(out var signingKey);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(signingKey, kid: signingKey.KeyId));

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        response.EnsureSuccessStatusCode();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task McpEndpoint_UnknownKid_ReturnsUnauthorized()
    {
        using var server = CreateServer(out var signingKey);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(signingKey, kid: "unknown-kid"));

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_MissingKid_ReturnsUnauthorized()
    {
        using var server = CreateServer(out var signingKey);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(signingKey, kid: null));

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private TestServer CreateServer(out RsaSecurityKey signingKey)
    {
        signingKey = CreateRsaKey("test-key");
        var manager = new FakeConfigurationManager(signingKey);
        var options = new GatewayAuthOptions(
            Issuer,
            Resource,
            Scope,
            OAuthRequireHttpsMetadata: false);

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(options);
                services.AddGatewayAuthentication(options);
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwtOptions =>
                {
                    jwtOptions.Configuration = null;
                    jwtOptions.ConfigurationManager = manager;
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

    private RsaSecurityKey CreateRsaKey(string kid)
    {
        var rsa = RSA.Create(2048);
        rsaKeys.Add(rsa);
        return new RsaSecurityKey(rsa) { KeyId = kid };
    }

    private static string CreateToken(RsaSecurityKey signingKey, string? kid)
    {
        var keyToUse = new RsaSecurityKey(signingKey.Rsa!.ExportParameters(true))
        {
            KeyId = kid
        };
        var issued = DateTime.UtcNow.AddSeconds(-10);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Resource,
            IssuedAt = issued,
            NotBefore = issued,
            Expires = issued.AddMinutes(4),
            Claims = new Dictionary<string, object>
            {
                [GatewayAuthConventions.Claims.Subject] = "subject-1",
                [GatewayAuthConventions.Claims.Scope] = Scope
            },
            SigningCredentials = new SigningCredentials(keyToUse, SecurityAlgorithms.RsaSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed class FakeConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration configuration;

        public FakeConfigurationManager(params SecurityKey[] keys)
        {
            configuration = new OpenIdConnectConfiguration
            {
                Issuer = Issuer
            };
            foreach (var key in keys)
            {
                configuration.SigningKeys.Add(key);
            }
        }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
            Task.FromResult(configuration);

        public void RequestRefresh()
        {
        }
    }
}
