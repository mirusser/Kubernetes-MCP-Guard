using System.Security.Cryptography;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Tests.Fakes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.Configuration;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.McpGateway.Tests.UnitTests;

/// <summary>
/// Characterization tests for the IdentityModel ConfigurationManager behavior that both the
/// gateway and downstream validator rely on: strict kid matching requires TryAllIssuerSigningKeys=false,
/// and the manager returns last-known-good cached config when a background refresh fails.
/// </summary>
public sealed class JwksConfigurationManagerTests : IDisposable
{
    private const string Issuer = "https://issuer.example.com";
    private const string JwksUri = "http://127.0.0.1:3010/realms/test/protocol/openid-connect/certs";
    private const string MetadataAddress = "http://127.0.0.1:3010/realms/test/.well-known/openid-configuration";

    private readonly List<RSA> rsaKeys = [];

    public void Dispose()
    {
        foreach (var rsa in rsaKeys)
        {
            rsa.Dispose();
        }
    }

    [Fact]
    public async Task TokenValidationParameters_DefaultTryAllIssuerSigningKeys_AcceptsTokenWithUnknownKid()
    {
        var configuredKey = CreateRsaKey("configured-key");
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = GatewayAuthConventions.DefaultOAuthResource,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = [configuredKey]
        };

        string token = CreateTokenWithKid(configuredKey, "unknown-kid");
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task TokenValidationParameters_TryAllIssuerSigningKeysFalse_RejectsTokenWithUnknownKid()
    {
        var configuredKey = CreateRsaKey("configured-key");
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = GatewayAuthConventions.DefaultOAuthResource,
            ValidateIssuerSigningKey = true,
            TryAllIssuerSigningKeys = false,
            IssuerSigningKeys = [configuredKey]
        };

        string token = CreateTokenWithKid(configuredKey, "unknown-kid");
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task TokenValidationParameters_TryAllIssuerSigningKeysFalse_RejectsTokenWithMissingKid()
    {
        var configuredKey = CreateRsaKey("configured-key");
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = GatewayAuthConventions.DefaultOAuthResource,
            ValidateIssuerSigningKey = true,
            TryAllIssuerSigningKeys = false,
            IssuerSigningKeys = [configuredKey]
        };

        string token = CreateTokenWithMissingKid(configuredKey);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ConfigurationManager_FetchFailureAfterSuccess_ReturnsCachedConfig()
    {
        var key = CreateRsaKey("key-a");
        var retriever = new CountingDocumentRetriever(
            CreateDiscoveryDocument(),
            CreateRsaJwks(key));

        var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            retriever)
        {
            AutomaticRefreshInterval = TimeSpan.FromMinutes(5),
            RefreshInterval = TimeSpan.FromSeconds(1)
        };

        var first = await manager.GetConfigurationAsync(CancellationToken.None);
        Assert.Contains(first.SigningKeys, k => k.KeyId == key.KeyId);

        retriever.FailNext();
        manager.RequestRefresh();
        using var refreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await retriever.WaitForFetchCountAsync(3, refreshCts.Token);
        var second = await manager.GetConfigurationAsync(CancellationToken.None);

        Assert.Contains(second.SigningKeys, k => k.KeyId == key.KeyId);
        Assert.True(retriever.FetchCount > 2, "A background refresh attempt should have occurred.");
    }

    [Fact]
    public async Task ConfigurationManager_KeyRollover_PicksUpRotatedKeyWithinRefreshInterval()
    {
        var keyA = CreateRsaKey("key-a");
        var keyB = CreateRsaKey("key-b");
        var retriever = new CountingDocumentRetriever(
            CreateDiscoveryDocument(),
            CreateRsaJwks(keyA),
            CreateRsaJwks(keyB));

        var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            retriever)
        {
            AutomaticRefreshInterval = TimeSpan.FromMinutes(5),
            RefreshInterval = TimeSpan.FromSeconds(1)
        };

        var first = await manager.GetConfigurationAsync(CancellationToken.None);
        Assert.Contains(first.SigningKeys, k => k.KeyId == keyA.KeyId);
        Assert.DoesNotContain(first.SigningKeys, k => k.KeyId == keyB.KeyId);

        var updateHandler = new ConfigurationUpdateHandler();
        manager.ConfigurationEventHandler = updateHandler;
        manager.RequestRefresh();
        var second = await updateHandler.Updated.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System);
        Assert.Contains(second.SigningKeys, k => k.KeyId == keyB.KeyId);
        Assert.DoesNotContain(second.SigningKeys, k => k.KeyId == keyA.KeyId);
    }

    private static string CreateDiscoveryDocument() =>
        $"{{\"issuer\":\"{Issuer}\",\"jwks_uri\":\"{JwksUri}\"}}";

    private static string CreateRsaJwks(RsaSecurityKey key)
    {
        var parameters = key.Rsa!.ExportParameters(false);
        string n = Base64UrlEncoder.Encode(parameters.Modulus!);
        string e = Base64UrlEncoder.Encode(parameters.Exponent!);
        return $"{{\"keys\":[{{\"kty\":\"RSA\",\"n\":\"{n}\",\"e\":\"{e}\",\"kid\":\"{key.KeyId}\",\"alg\":\"RS256\",\"use\":\"sig\"}}]}}";
    }

    private RsaSecurityKey CreateRsaKey(string kid)
    {
        var rsa = RSA.Create(2048);
        rsaKeys.Add(rsa);
        return new RsaSecurityKey(rsa) { KeyId = kid };
    }

    private static string CreateTokenWithKid(RsaSecurityKey signingKey, string kid)
    {
        var keyWithKid = new RsaSecurityKey(signingKey.Rsa!.ExportParameters(true)) { KeyId = kid };
        return CreateTokenCore(keyWithKid);
    }

    private static string CreateTokenWithMissingKid(RsaSecurityKey signingKey)
    {
        var keyWithoutKid = new RsaSecurityKey(signingKey.Rsa!.ExportParameters(true));
        return CreateTokenCore(keyWithoutKid);
    }

    private static string CreateTokenCore(SecurityKey key)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = GatewayAuthConventions.DefaultOAuthResource,
            Expires = DateTime.UtcNow.AddMinutes(2),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                [GatewayAuthConventions.Claims.Scope] = GatewayAuthConventions.DefaultOAuthScope
            }
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed class ConfigurationUpdateHandler : IConfigurationEventHandler<OpenIdConnectConfiguration>
    {
        private readonly TaskCompletionSource<OpenIdConnectConfiguration> updated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task<OpenIdConnectConfiguration> Updated => updated.Task;

        public Task<ConfigurationEventHandlerResult<OpenIdConnectConfiguration>> BeforeRetrieveAsync(
            string metadataAddress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ConfigurationEventHandlerResult<OpenIdConnectConfiguration>.NoResult);

        public Task AfterUpdateAsync(
            string metadataAddress,
            OpenIdConnectConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            updated.TrySetResult(configuration);
            return Task.CompletedTask;
        }
    }

}
