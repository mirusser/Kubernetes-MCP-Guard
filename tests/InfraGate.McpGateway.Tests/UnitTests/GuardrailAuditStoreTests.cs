using System.Text.Json;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GuardrailAuditStoreTests
{
    [Fact]
    public async Task WriteAsync_IncludesSubjectWithoutCredentials()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-guard-tests", Guid.NewGuid().ToString("N"));
        var store = new GuardrailAuditStore(new McpGatewayOptions(
            new GatewayAuthOptions("https://issuer.example.com"),
            "downstream.csproj",
            root,
            Directory.GetCurrentDirectory(),
            Path.Combine(root, "approvals"),
            ApprovalBaseUrl: null,
            McpGatewayOptions.DefaultApprovalChallengeTtl));

        await store.WriteAsync(
            new GuardrailAuditEvent(
                "request_apply_manifest",
                "request",
                "warn",
                ["tool-use"],
                "plan-1",
                "ada",
                "oauth-jwt"),
            CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(root, "audit.jsonl"));
        using var document = JsonDocument.Parse(json);
        var rootElement = document.RootElement;

        Assert.Equal("ada", rootElement.GetProperty("subject").GetString());
        Assert.Equal("oauth-jwt", rootElement.GetProperty("authenticationType").GetString());
        Assert.False(rootElement.TryGetProperty("authorization", out _));
        Assert.DoesNotContain("Bearer ", json);
    }
}
