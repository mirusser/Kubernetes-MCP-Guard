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

    [Fact]
    public async Task WriteAsync_AppendsMultipleEventsOnSeparateLines()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-guard-tests", Guid.NewGuid().ToString("N"));
        var store = CreateStore(root);

        await store.WriteAsync(
            new GuardrailAuditEvent("tool-a", "request", "warn", [], null, "alice", null),
            CancellationToken.None);
        await store.WriteAsync(
            new GuardrailAuditEvent("tool-b", "response", "allow", [], null, "bob", null),
            CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(Path.Combine(root, "audit.jsonl"));
        var nonEmpty = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        Assert.Equal(2, nonEmpty.Length);
        using var first = JsonDocument.Parse(nonEmpty[0]);
        using var second = JsonDocument.Parse(nonEmpty[1]);
        Assert.Equal("tool-a", first.RootElement.GetProperty("toolName").GetString());
        Assert.Equal("tool-b", second.RootElement.GetProperty("toolName").GetString());
    }

    [Fact]
    public async Task WriteAsync_IncludesToolNameDirectionActionAndCategories()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-guard-tests", Guid.NewGuid().ToString("N"));
        var store = CreateStore(root);

        await store.WriteAsync(
            new GuardrailAuditEvent(
                "request_apply_manifest",
                "request",
                "deny",
                ["tool-use", "sensitive"],
                null,
                "alice",
                null),
            CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(root, "audit.jsonl"));
        using var document = JsonDocument.Parse(json);
        var root2 = document.RootElement;

        Assert.Equal("request_apply_manifest", root2.GetProperty("toolName").GetString());
        Assert.Equal("request", root2.GetProperty("direction").GetString());
        Assert.Equal("deny", root2.GetProperty("action").GetString());
        Assert.Equal(2, root2.GetProperty("categories").GetArrayLength());
    }

    [Fact]
    public async Task WriteAsync_IncludesPlanIdWhenPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-guard-tests", Guid.NewGuid().ToString("N"));
        var store = CreateStore(root);

        await store.WriteAsync(
            new GuardrailAuditEvent("tool", "request", "warn", [], "plan-42", "alice", null),
            CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(root, "audit.jsonl"));
        using var document = JsonDocument.Parse(json);

        Assert.Equal("plan-42", document.RootElement.GetProperty("planId").GetString());
    }

    private static GuardrailAuditStore CreateStore(string root) =>
        new(new McpGatewayOptions(
            new GatewayAuthOptions("https://issuer.example.com"),
            "downstream.csproj",
            root,
            Directory.GetCurrentDirectory(),
            Path.Combine(root, "approvals"),
            ApprovalBaseUrl: null,
            McpGatewayOptions.DefaultApprovalChallengeTtl));
}
