using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

// ASPDEPR004/ASPDEPR008: WebHostBuilder + TestServer are deprecated in favor of WebApplicationBuilder.
// Suppressed because: WebApplicationFactory<T> requires a public Program class — overkill for isolated auth tests.
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
    [InlineData("mcp:tools.propose")]
    [InlineData("mcp:tools.execute")]
    public async Task McpEndpoint_AllowsServiceToolScopes(string scope)
    {
        using var server = CreateOAuthServer(out var signingKey);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(signingKey, scope: scope));

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        response.EnsureSuccessStatusCode();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task McpEndpoint_WithIntrospectionEnabled_AllowsActiveJwt()
    {
        var activityValidator = new FakeTokenActivityValidator(isActive: true);
        var options = new GatewayAuthOptions(
            Issuer,
            Resource,
            Scope,
            TokenIntrospectionEnabled: true,
            TokenIntrospectionClientId: "introspection-client",
            TokenIntrospectionClientSecret: "secret-placeholder");
        using var server = CreateOAuthServer(out var signingKey, options, services =>
        {
            services.AddSingleton<ITokenActivityValidator>(activityValidator);
        });
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(signingKey));

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        response.EnsureSuccessStatusCode();
        Assert.Equal(1, activityValidator.CallCount);
    }

    [Fact]
    public async Task McpEndpoint_WithIntrospectionEnabled_RejectsInactiveJwt()
    {
        var activityValidator = new FakeTokenActivityValidator(isActive: false);
        var options = new GatewayAuthOptions(
            Issuer,
            Resource,
            Scope,
            TokenIntrospectionEnabled: true,
            TokenIntrospectionClientId: "introspection-client",
            TokenIntrospectionClientSecret: "secret-placeholder");
        using var server = CreateOAuthServer(out var signingKey, options, services =>
        {
            services.AddSingleton<ITokenActivityValidator>(activityValidator);
        });
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(signingKey));

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, activityValidator.CallCount);
    }

    [Fact]
    public async Task McpEndpoint_TokenLifetimeExceedsConfiguredMaximum_ReturnsUnauthorized()
    {
        using var server = CreateOAuthServer(out var signingKey);
        using var client = server.CreateClient();
        var issuedAt = DateTime.UtcNow.AddMinutes(-10);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(signingKey, issuedAt: issuedAt, notBefore: issuedAt, expires: issuedAt.AddMinutes(10)));

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void HasAcceptedAccessTokenLifetime_MissingIssuedAtAndNotBefore_ReturnsFalse()
    {
        var token = new JsonWebToken(CreateUnsignedJwtWithoutBaseline());

        bool accepted = GatewayAuthentication.HasAcceptedAccessTokenLifetime(token, maxAcceptedLifetimeSeconds: 300);

        Assert.False(accepted);
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
        const string metadataAddress = "http://issuer.internal/.well-known/openid-configuration";
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

    // ── FromConfiguration binding ──────────────────────────────────────────

    [Fact]
    public void AddGatewayAuthentication_ApprovalOAuth_DoesNotPersistTokensInCookie()
    {
        using var server = CreateOAuthServer(out _);
        var monitor = server.Host.Services.GetRequiredService<IOptionsMonitor<OAuthOptions>>();
        var oauthOptions = monitor.Get(GatewayAuthConventions.Schemes.ApprovalOAuth);

        Assert.False(oauthOptions.SaveTokens);
    }

    [Fact]
    public void FromConfiguration_RequireDPoP_IsBoundWhenTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GatewayAuthConventions.ConfigurationKeys.OAuthAuthority] = Issuer,
                [GatewayAuthConventions.ConfigurationKeys.RequireDPoP] = "true"
            })
            .Build();

        var options = GatewayAuthOptions.FromConfiguration(config);

        Assert.True(options.RequireDPoP);
    }

    [Fact]
    public void FromConfiguration_RequireDPoP_DefaultsFalseWhenAbsent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GatewayAuthConventions.ConfigurationKeys.OAuthAuthority] = Issuer
            })
            .Build();

        var options = GatewayAuthOptions.FromConfiguration(config);

        Assert.False(options.RequireDPoP);
    }

    // ── DPoP integration tests ──────────────────────────────────────────────

    [Fact]
    public async Task McpEndpoint_WithDpopRequired_RejectsBearerToken()
    {
        using var dpopFactory = new DpopProofTestFactory();
        using var server = CreateDpopServer(out var signingKey);
        using var client = server.CreateClient();
        var accessToken = dpopFactory.CreateDpopBoundAccessToken(signingKey, Issuer, Resource, Scope);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WithDpopRequired_RejectsDpopTokenWithoutProofHeader()
    {
        using var dpopFactory = new DpopProofTestFactory();
        using var server = CreateDpopServer(out var signingKey);
        using var client = server.CreateClient();
        var accessToken = dpopFactory.CreateDpopBoundAccessToken(signingKey, Issuer, Resource, Scope);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(GatewayAuthConventions.DPoP.Scheme, accessToken);

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-ath")]
    [InlineData("wrong-htm")]
    [InlineData("wrong-htu")]
    [InlineData("expired-iat")]
    [InlineData("wrong-key")]
    public async Task McpEndpoint_WithDpopRequired_RejectsInvalidDpopProof(string invalidCase)
    {
        using var dpopFactory = new DpopProofTestFactory();
        using var server = CreateDpopServer(out var signingKey);
        using var client = server.CreateClient();
        var uri = $"http://localhost{McpGatewayConventions.McpPath}";
        var accessToken = dpopFactory.CreateDpopBoundAccessToken(signingKey, Issuer, Resource, Scope);
        var proof = invalidCase switch
        {
            "wrong-ath" => dpopFactory.CreateDpopProof(accessToken, uri: uri, overrideAth: "aW52YWxpZA"),
            "wrong-htm" => dpopFactory.CreateDpopProof(accessToken, uri: uri, overrideHtm: "POST"),
            "wrong-htu" => dpopFactory.CreateDpopProof(accessToken, uri: uri, overrideHtu: "http://other.example.com/mcp"),
            "expired-iat" => dpopFactory.CreateDpopProof(accessToken, uri: uri, iatOffsetSeconds: -400),
            "wrong-key" => dpopFactory.CreateDpopProofWithWrongKey(accessToken, uri: uri),
            _ => throw new InvalidOperationException()
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(GatewayAuthConventions.DPoP.Scheme, accessToken);
        client.DefaultRequestHeaders.Add(GatewayAuthConventions.DPoP.ProofHeaderName, proof);

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WithDpopRequired_RejectsReusedProofJti()
    {
        using var dpopFactory = new DpopProofTestFactory();
        using var server = CreateDpopServer(out var signingKey);
        var uri = $"http://localhost{McpGatewayConventions.McpPath}";
        var accessToken = dpopFactory.CreateDpopBoundAccessToken(signingKey, Issuer, Resource, Scope);
        const string sharedJti = "replay-test-jti";
        var proof = dpopFactory.CreateDpopProof(accessToken, uri: uri, jti: sharedJti);

        using var client1 = server.CreateClient();
        client1.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(GatewayAuthConventions.DPoP.Scheme, accessToken);
        client1.DefaultRequestHeaders.Add(GatewayAuthConventions.DPoP.ProofHeaderName, proof);
        var first = await client1.GetAsync(McpGatewayConventions.McpPath);
        first.EnsureSuccessStatusCode();

        using var client2 = server.CreateClient();
        client2.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(GatewayAuthConventions.DPoP.Scheme, accessToken);
        client2.DefaultRequestHeaders.Add(GatewayAuthConventions.DPoP.ProofHeaderName, proof);
        var second = await client2.GetAsync(McpGatewayConventions.McpPath);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WithDpopRequired_AllowsValidDpopBoundToken()
    {
        using var dpopFactory = new DpopProofTestFactory();
        using var server = CreateDpopServer(out var signingKey);
        using var client = server.CreateClient();
        var uri = $"http://localhost{McpGatewayConventions.McpPath}";
        var accessToken = dpopFactory.CreateDpopBoundAccessToken(signingKey, Issuer, Resource, Scope);
        var proof = dpopFactory.CreateDpopProof(accessToken, uri: uri);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(GatewayAuthConventions.DPoP.Scheme, accessToken);
        client.DefaultRequestHeaders.Add(GatewayAuthConventions.DPoP.ProofHeaderName, proof);

        var response = await client.GetAsync(McpGatewayConventions.McpPath);

        response.EnsureSuccessStatusCode();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    private static TestServer CreateOAuthServer() =>
        CreateOAuthServer(out _);

    private static TestServer CreateOAuthServer(out SecurityKey signingKey) =>
        CreateOAuthServer(out signingKey, options: null, configureServices: null);

    private static TestServer CreateOAuthServer(
        out SecurityKey signingKey,
        GatewayAuthOptions? options,
        Action<IServiceCollection>? configureServices)
    {
        var key = new SymmetricSecurityKey("0123456789abcdef0123456789abcdef"u8.ToArray())
        {
            KeyId = "test-key"
        };
        signingKey = key;
        options ??= new GatewayAuthOptions(
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
                configureServices?.Invoke(services);
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

    private static TestServer CreateDpopServer(out SecurityKey signingKey)
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
            OAuthRequireHttpsMetadata: false,
            RequireDPoP: true);

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(options);
                services.AddGatewayAuthentication(options);
                services.AddSingleton<InfraGate.McpGateway.Auth.Dpop.IDpopProofReplayStore, InfraGate.McpGateway.Auth.Dpop.InMemoryDpopProofReplayStore>();
                services.AddSingleton<InfraGate.McpGateway.Auth.Dpop.IDpopProofValidator, InfraGate.McpGateway.Auth.Dpop.DpopProofValidator>();
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
        DateTime? expires = null,
        DateTime? issuedAt = null,
        DateTime? notBefore = null)
    {
        var issued = issuedAt ?? DateTime.UtcNow.AddSeconds(-10);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issued,
            NotBefore = notBefore ?? issued,
            Expires = expires ?? issued.AddMinutes(4),
            Claims = new Dictionary<string, object>
            {
                [GatewayAuthConventions.Claims.Subject] = "subject-1",
                [GatewayAuthConventions.Claims.Scope] = scope
            },
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string CreateUnsignedJwtWithoutBaseline()
    {
        string header = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}"));
        long expiresAt = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        string payload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(
            $"{{\"{GatewayAuthConventions.Claims.Expiration}\":{expiresAt}}}"));
        return $"{header}.{payload}.";
    }

    private sealed class FakeTokenActivityValidator(bool isActive) : ITokenActivityValidator
    {
        public int CallCount { get; private set; }

        public Task<bool> IsActiveAsync(JsonWebToken accessToken, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(accessToken);
            CallCount++;
            return Task.FromResult(isActive);
        }
    }
}
