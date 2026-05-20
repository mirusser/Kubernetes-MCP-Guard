using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using InfraGate.DownstreamAuth;
using InfraGate.McpServer.DownstreamAuth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.McpServer.Tests.UnitTests.DownstreamAuth;

/// <summary>
/// Tests for DownstreamTokenValidator using locally-signed JWTs (no real Keycloak).
/// </summary>
public sealed class DownstreamTokenValidatorTests : IDisposable
{
    private const string TestIssuer = "https://auth.example.com/realms/test";
    private const string TestAudience = "urn:infra-gate:mcp-server";
    private const string TestScope = "mcp:downstream";
    private const string TestGatewayClientId = "infra-gate-gateway";

    private readonly RsaSecurityKey signingKey;
    private readonly RsaSecurityKey wrongKey;
    private readonly DownstreamAuthOptions options;
    private readonly DownstreamTokenValidator validator;

    public DownstreamTokenValidatorTests()
    {
        signingKey = CreateRsaKey();
        wrongKey = CreateRsaKey();

        options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = TestIssuer,
            Audience = TestAudience,
            Scope = TestScope,
            GatewayClientId = TestGatewayClientId,
        };

        validator = new DownstreamTokenValidator(
            options,
            NullLogger<DownstreamTokenValidator>.Instance,
            staticKeys: [signingKey]);
    }

    public void Dispose()
    {
        signingKey.Rsa?.Dispose();
        wrongKey.Rsa?.Dispose();
    }

    // Test 1: Valid token passes validation
    [Fact]
    public async Task ValidateAsync_ValidToken_ReturnsSuccess()
    {
        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: signingKey);

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Null(result.FailureReason);
    }

    // Test 2: Missing token (null/empty) is refused
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_NullOrEmptyToken_ReturnsFailure(string? token)
    {
        var result = await validator.ValidateAsync(token!, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    // Test 3: Wrong audience is refused
    [Fact]
    public async Task ValidateAsync_WrongAudience_ReturnsFailure()
    {
        string token = CreateToken(
            issuer: TestIssuer,
            audience: "urn:wrong:audience",
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: signingKey);

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    // Test 4: Wrong scope is refused
    [Fact]
    public async Task ValidateAsync_WrongScope_ReturnsFailure()
    {
        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: "wrong:scope",
            clientId: TestGatewayClientId,
            key: signingKey);

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    // Test 5: Expired token is refused
    [Fact]
    public async Task ValidateAsync_ExpiredToken_ReturnsFailure()
    {
        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: signingKey,
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddSeconds(-60)); // expired 60s ago

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    // Test 6: Token expired within 30s clock skew is accepted
    [Fact]
    public async Task ValidateAsync_ExpiredWithinClockSkew_ReturnsSuccess()
    {
        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: signingKey,
            notBefore: DateTime.UtcNow.AddHours(-1),
            expires: DateTime.UtcNow.AddSeconds(-20)); // expired 20s ago, within 30s skew

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.True(result.IsValid);
    }

    // Test 7: Token expired beyond 30s clock skew is refused
    [Fact]
    public async Task ValidateAsync_ExpiredBeyondClockSkew_ReturnsFailure()
    {
        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: signingKey,
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddSeconds(-40)); // expired 40s ago, beyond 30s skew

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    // Test 8: Invalid signature is refused
    [Fact]
    public async Task ValidateAsync_InvalidSignature_ReturnsFailure()
    {
        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: wrongKey); // signed with wrong key

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    // Test 9: Required=false bypasses validation entirely
    [Fact]
    public async Task ValidateAsync_RequiredFalse_BypassesValidation()
    {
        var disabledOptions = new DownstreamAuthOptions { Required = false };
        var disabledValidator = new DownstreamTokenValidator(
            disabledOptions,
            NullLogger<DownstreamTokenValidator>.Instance,
            staticKeys: []);

        // Even a completely invalid token should pass when Required=false
        var result = await disabledValidator.ValidateAsync("not-a-token", CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Null(result.FailureReason);
    }

    // Test 10: No token value appears in any failure reason or log
    [Fact]
    public async Task ValidateAsync_InvalidToken_FailureReasonDoesNotContainTokenValue()
    {
        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: "wrong:scope",
            clientId: TestGatewayClientId,
            key: signingKey);

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        // The failure reason must not contain the raw token string
        Assert.DoesNotContain(token, result.FailureReason ?? "");
    }

    // Test 11: Wrong client ID (azp) is refused when GatewayClientId is configured
    [Fact]
    public async Task ValidateAsync_WrongClientId_ReturnsFailure()
    {
        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: TestScope,
            clientId: "wrong-client",
            key: signingKey);

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    // Test 12: When GatewayClientId is empty, client ID check is skipped
    [Fact]
    public async Task ValidateAsync_NoGatewayClientIdConfigured_SkipsClientIdCheck()
    {
        var noClientIdOptions = new DownstreamAuthOptions
        {
            Required = true,
            Authority = TestIssuer,
            Audience = TestAudience,
            Scope = TestScope,
            GatewayClientId = string.Empty,
        };
        var noClientIdValidator = new DownstreamTokenValidator(
            noClientIdOptions,
            NullLogger<DownstreamTokenValidator>.Instance,
            staticKeys: [signingKey]);

        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: TestScope,
            clientId: "any-client",
            key: signingKey);

        var result = await noClientIdValidator.ValidateAsync(token, CancellationToken.None);

        Assert.True(result.IsValid);
    }

    private static string CreateToken(
        string issuer,
        string audience,
        string scope,
        string clientId,
        RsaSecurityKey key,
        DateTime? notBefore = null,
        DateTime? expires = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var claims = new List<Claim>
        {
            new Claim("scope", scope),
            new Claim("azp", clientId),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        };

        var token = handler.CreateToken(descriptor);
        return handler.WriteToken(token);
    }

    private static RsaSecurityKey CreateRsaKey()
    {
        var rsa = RSA.Create(2048);
        return new RsaSecurityKey(rsa);
    }
}
