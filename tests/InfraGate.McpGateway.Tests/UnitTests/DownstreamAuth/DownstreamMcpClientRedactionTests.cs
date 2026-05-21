using InfraGate.DownstreamAuth;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.DownstreamAuth;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;

namespace InfraGate.McpGateway.Tests.UnitTests.DownstreamAuth;

/// <summary>
/// Verifies that DownstreamMcpClient never leaks token values in log messages or
/// McpException messages exposed to callers.
/// </summary>
public sealed class DownstreamMcpClientRedactionTests
{
    private const string SensitiveTokenValue = "Bearer eyJhbGciOiJSUzI1NiJ9.very-sensitive-payload.sig";

    [Fact]
    public async Task WithAuthRetryAsync_AuthRejectedAfterRefresh_TokenValueNotInMcpExceptionMessage()
    {
        // Both the initial and refreshed tokens carry a sensitive value.
        // The final McpException must NOT echo either.
        var tokenProvider = new FakeDownstreamServiceTokenProvider("Bearer first-token", SensitiveTokenValue);
        var logger = new CapturingLogger<DownstreamMcpClient>();
        var client = CreateDownstreamMcpClient(tokenProvider, logger);

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            client.WithAuthRetryAsync<string>(_ =>
            {
                throw new McpException(
                    $"{DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired}: rejected");
            }, CancellationToken.None));

        Assert.DoesNotContain(SensitiveTokenValue, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer first-token", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAuthRetryAsync_AuthRejectedAfterRefresh_LogWarningDoesNotContainTokenValue()
    {
        var tokenProvider = new FakeDownstreamServiceTokenProvider("Bearer first-token", SensitiveTokenValue);
        var logger = new CapturingLogger<DownstreamMcpClient>();
        var client = CreateDownstreamMcpClient(tokenProvider, logger);

        await Assert.ThrowsAsync<McpException>(() =>
            client.WithAuthRetryAsync<string>(_ =>
            {
                throw new McpException(
                    $"{DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired}: rejected");
            }, CancellationToken.None));

        foreach (string message in logger.Messages)
        {
            Assert.DoesNotContain(SensitiveTokenValue, message, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer first-token", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildAuthMeta_WithToken_TokenValueStoredAsMetaValue_NotAsKey()
    {
        // Token is placed as a value under DownstreamAuthConventions.MetaKey.
        // The key name itself must never be the token value — otherwise iterating
        // arguments.Keys in the error log path would expose the token.
        string token = SensitiveTokenValue;

        var meta = DownstreamMcpClient.BuildAuthMeta(token);

        Assert.NotNull(meta);
        // Keys must not contain the sensitive token
        foreach (string key in meta!.Select(kv => kv.Key))
        {
            Assert.DoesNotContain(token, key, StringComparison.Ordinal);
        }

        // The convention key is the only key
        Assert.True(meta.ContainsKey(DownstreamAuthConventions.MetaKey));
    }

    private static DownstreamMcpClient CreateDownstreamMcpClient(
        IDownstreamServiceTokenProvider tokenProvider,
        CapturingLogger<DownstreamMcpClient> logger)
    {
        var authOptions = new GatewayAuthOptions(
            OAuthAuthority: "http://127.0.0.1:3010/realms/infra-gate",
            OAuthResource: GatewayAuthConventions.DefaultOAuthResource,
            OAuthScope: GatewayAuthConventions.DefaultOAuthScope,
            OAuthRequireHttpsMetadata: false);

        var options = new McpGatewayOptions(
            authOptions,
            DownstreamProject: "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj",
            GuardAuditRoot: Path.Combine(Path.GetTempPath(), "audit"),
            WorkingDirectory: Directory.GetCurrentDirectory(),
            ApprovalRoot: Path.Combine(Path.GetTempPath(), "approvals"),
            ApprovalBaseUrl: null,
            ApprovalChallengeTtl: McpGatewayOptions.DefaultApprovalChallengeTtl,
            DownstreamAssembly: null);

        return new DownstreamMcpClient(options, tokenProvider, logger, NullLoggerFactory.Instance);
    }
}
