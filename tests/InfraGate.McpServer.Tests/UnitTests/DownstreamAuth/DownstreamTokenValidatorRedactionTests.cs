using System.IdentityModel.Tokens.Jwt;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using InfraGate.DownstreamAuth;
using InfraGate.McpServer.DownstreamAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpServer.Tests.UnitTests.DownstreamAuth;

/// <summary>
/// Verifies that raw token values never appear in DownstreamTokenValidator failure reasons,
/// DownstreamAuthFilter McpException messages, or log output.
/// </summary>
public sealed class DownstreamTokenValidatorRedactionTests : IDisposable
{
    private const string TestIssuer = "https://auth.example.com/realms/test";
    private const string TestAudience = "urn:infra-gate:mcp-server";
    private const string TestScope = "mcp:downstream";
    private const string TestGatewayClientId = "infra-gate-gateway";

    private readonly RsaSecurityKey signingKey;
    private readonly RsaSecurityKey wrongKey;
    private readonly DownstreamAuthOptions options;

    public DownstreamTokenValidatorRedactionTests()
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
    }

    public void Dispose()
    {
        signingKey.Rsa?.Dispose();
        wrongKey.Rsa?.Dispose();
    }

    // Test 1: Expired token — failure reason does not contain raw token value
    [Fact]
    public async Task ValidateAsync_ExpiredToken_FailureReasonDoesNotContainRawTokenValue()
    {
        var logger = new CapturingLogger<DownstreamTokenValidator>();
        var validator = new DownstreamTokenValidator(options, logger, staticKeys: [signingKey]);

        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: signingKey,
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddSeconds(-120)); // expired, beyond 30s skew

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.DoesNotContain(token, result.FailureReason ?? string.Empty, StringComparison.Ordinal);
    }

    // Test 2: Wrong audience — failure reason does not contain raw token value
    [Fact]
    public async Task ValidateAsync_WrongAudience_FailureReasonDoesNotContainRawTokenValue()
    {
        var logger = new CapturingLogger<DownstreamTokenValidator>();
        var validator = new DownstreamTokenValidator(options, logger, staticKeys: [signingKey]);

        string token = CreateToken(
            issuer: TestIssuer,
            audience: "urn:attacker:audience",
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: signingKey);

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.DoesNotContain(token, result.FailureReason ?? string.Empty, StringComparison.Ordinal);
    }

    // Test 3: Wrong signature — failure reason does not contain raw token value
    [Fact]
    public async Task ValidateAsync_WrongSignature_FailureReasonDoesNotContainRawTokenValue()
    {
        var logger = new CapturingLogger<DownstreamTokenValidator>();
        var validator = new DownstreamTokenValidator(options, logger, staticKeys: [signingKey]);

        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: wrongKey); // signed with wrong key

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.DoesNotContain(token, result.FailureReason ?? string.Empty, StringComparison.Ordinal);
    }

    // Test 4: Missing scope — failure reason does not contain raw token value
    [Fact]
    public async Task ValidateAsync_MissingScope_FailureReasonDoesNotContainRawTokenValue()
    {
        var logger = new CapturingLogger<DownstreamTokenValidator>();
        var validator = new DownstreamTokenValidator(options, logger, staticKeys: [signingKey]);

        string token = CreateToken(
            issuer: TestIssuer,
            audience: TestAudience,
            scope: "wrong:scope",
            clientId: TestGatewayClientId,
            key: signingKey);

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.DoesNotContain(token, result.FailureReason ?? string.Empty, StringComparison.Ordinal);
    }

    // Test 5: Validator log messages never contain the raw token value for any failure type
    [Fact]
    public async Task ValidateAsync_AnyFailure_LogMessagesDoNotContainRawTokenValue()
    {
        var logger = new CapturingLogger<DownstreamTokenValidator>();
        var validator = new DownstreamTokenValidator(options, logger, staticKeys: [signingKey]);

        string token = CreateToken(
            issuer: TestIssuer,
            audience: "urn:wrong:audience",
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: signingKey);

        await validator.ValidateAsync(token, CancellationToken.None);

        foreach (string message in logger.Messages)
        {
            Assert.DoesNotContain(token, message, StringComparison.Ordinal);
        }
    }

    // Test 6: McpException from DownstreamAuthFilter does not contain the raw token value
    [Fact]
    public async Task DownstreamAuthFilter_InvalidToken_McpExceptionDoesNotContainRawTokenValue()
    {
        var validator = new DownstreamTokenValidator(options,
            NullLogger<DownstreamTokenValidator>.Instance, staticKeys: [signingKey]);

        // Build a token with wrong audience so validation fails
        string rawToken = CreateToken(
            issuer: TestIssuer,
            audience: "urn:wrong:audience",
            scope: TestScope,
            clientId: TestGatewayClientId,
            key: signingKey);

        // Build a meta object containing the bearer token as the filter expects
        string bearerToken = DownstreamAuthConventions.BearerPrefix + rawToken;
        var meta = new System.Text.Json.Nodes.JsonObject
        {
            [DownstreamAuthConventions.MetaKey] = bearerToken
        };

        var services = new ServiceCollection()
            .AddSingleton(validator)
            .BuildServiceProvider();

        var request = (RequestContext<CallToolRequestParams>)RuntimeHelpers.GetUninitializedObject(
            typeof(RequestContext<CallToolRequestParams>));
        request.Params = new CallToolRequestParams { Name = "test_tool", Meta = meta };
        request.Services = services;

        var filter = DownstreamAuthFilter.CallTool();
        McpRequestHandler<CallToolRequestParams, CallToolResult> next =
            (_, _) => new ValueTask<CallToolResult>(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "ok" }]
            });
        var handler = filter(next);

        var ex = await Assert.ThrowsAsync<McpException>(() => handler(request, CancellationToken.None).AsTask());

        // Neither the raw JWT nor the bearer-prefixed token must appear in the McpException message
        Assert.DoesNotContain(rawToken, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(bearerToken, ex.Message, StringComparison.Ordinal);
    }

    private string CreateToken(
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
            new("scope", scope),
            new("azp", clientId),
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
