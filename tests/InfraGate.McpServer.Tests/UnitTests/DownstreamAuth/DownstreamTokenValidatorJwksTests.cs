using System.Security.Cryptography;
using InfraGate.DownstreamAuth;
using InfraGate.McpServer.DownstreamAuth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.McpServer.Tests.UnitTests.DownstreamAuth;

/// <summary>
/// Verifies that <see cref="DownstreamTokenValidator"/> resolves signing keys from the
/// configured <see cref="IConfigurationManager{OpenIdConnectConfiguration}"/> and handles
/// key rotation by trusting whatever configuration the manager returns.
/// </summary>
public sealed class DownstreamTokenValidatorJwksTests : IDisposable
{
    private const string TestIssuer = "https://auth.example.com/realms/test";
    private const string TestAudience = "urn:infra-gate:mcp-server";
    private const string TestScope = "mcp:downstream";

    private readonly List<RSA> rsaKeys = [];

    public void Dispose()
    {
        foreach (var rsa in rsaKeys)
        {
            rsa.Dispose();
        }
    }

    [Fact]
    public async Task ValidateAsync_ConfigurationManagerReturnsConfig_UsesSigningKeysFromConfig()
    {
        var keyA = CreateRsaKey("key-a");
        var manager = new SequencedConfigurationManager(CreateConfiguration(keyA));
        var validator = CreateValidator(manager);
        string token = CreateToken(keyA);

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ConfigurationManagerReturnsRotatedConfig_RejectsOldKeyAndAcceptsNewKey()
    {
        var keyA = CreateRsaKey("key-a");
        var keyB = CreateRsaKey("key-b");
        var manager = new SequencedConfigurationManager(
            CreateConfiguration(keyA),
            CreateConfiguration(keyB),
            CreateConfiguration(keyB));
        var validator = CreateValidator(manager);
        string tokenA = CreateToken(keyA);
        string tokenB = CreateToken(keyB);

        var first = await validator.ValidateAsync(tokenA, CancellationToken.None);
        Assert.True(first.IsValid);

        var second = await validator.ValidateAsync(tokenA, CancellationToken.None);
        Assert.False(second.IsValid);

        var third = await validator.ValidateAsync(tokenB, CancellationToken.None);
        Assert.True(third.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ConfigurationManagerThrows_ReturnsFailure()
    {
        var manager = new ThrowingConfigurationManager();
        var validator = CreateValidator(manager);
        string token = "not-a-valid-token";

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    private DownstreamTokenValidator CreateValidator(IConfigurationManager<OpenIdConnectConfiguration> manager)
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = TestIssuer,
            Audience = TestAudience,
            Scope = TestScope,
            RequireHttpsMetadata = false
        };

        return new DownstreamTokenValidator(
            options,
            NullLogger<DownstreamTokenValidator>.Instance,
            manager);
    }

    private RsaSecurityKey CreateRsaKey(string kid)
    {
        var rsa = RSA.Create(2048);
        rsaKeys.Add(rsa);
        return new RsaSecurityKey(rsa) { KeyId = kid };
    }

    private static OpenIdConnectConfiguration CreateConfiguration(params SecurityKey[] keys)
    {
        var config = new OpenIdConnectConfiguration
        {
            Issuer = TestIssuer
        };
        foreach (var key in keys)
        {
            config.SigningKeys.Add(key);
        }

        return config;
    }

    private static string CreateToken(RsaSecurityKey signingKey)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(5),
            Subject = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("scope", TestScope)
            ]),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed class SequencedConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly Queue<OpenIdConnectConfiguration> configs;

        public SequencedConfigurationManager(params OpenIdConnectConfiguration[] configs)
        {
            this.configs = new Queue<OpenIdConnectConfiguration>(configs);
        }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
        {
            if (configs.Count == 0)
            {
                return Task.FromException<OpenIdConnectConfiguration>(
                    new InvalidOperationException("No more configurations in sequence."));
            }

            return Task.FromResult(configs.Dequeue());
        }

        public void RequestRefresh()
        {
        }
    }

    private sealed class ThrowingConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
            Task.FromException<OpenIdConnectConfiguration>(
                new InvalidOperationException("JWKS fetch failed"));

        public void RequestRefresh()
        {
        }
    }
}
