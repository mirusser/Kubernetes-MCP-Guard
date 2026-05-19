using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesPlanBuilderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly PlanRequester TestRequester = new("test-subject", "oauth-jwt");

    private static K8sPlanDryRun MakeDryRun(string ns, string name) =>
        new(
            "succeeded",
            DateTimeOffset.UtcNow,
            [new K8sPlanDryRunObject($"apps/v1 Deployment {ns}/{name}", "{}")],
            [],
            "Server-side dry-run succeeded.");

    private static K8sPlanDiff MakeDiff(string ns, string name) =>
        new(
            new K8sObjectRef("apps/v1", "Deployment", ns, name),
            "update",
            $"Update apps/v1 Deployment {ns}/{name}",
            "@@ -1 +1 @@",
            "{}",
            "{}",
            [],
            [],
            []);

    private static string DryRunJson(K8sPlanDryRun dryRun) =>
        JsonSerializer.Serialize(dryRun, JsonOptions);

    private static string DiffJson(K8sPlanDiff[] diffs) =>
        JsonSerializer.Serialize(diffs, JsonOptions);

    private static string ApplyEvidenceJson(K8sPlanDryRun dryRun) =>
        JsonSerializer.Serialize(
            new K8sApplyEvidence(dryRun, [], false, null),
            JsonOptions);

    private static string ApplyEvidenceBlockedJson(K8sPlanDryRun dryRun, string refusal) =>
        JsonSerializer.Serialize(
            new K8sApplyEvidence(dryRun, [], true, refusal),
            JsonOptions);

    [Fact]
    public async Task BuildAsync_ApplyManifest_HappyPath_ReturnsPlanWithEnvelope()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_apply_manifest", ApplyEvidenceJson(dryRun))
            .With("diff_manifest", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "apply_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Envelope);
        Assert.NotEmpty(result.PlanId);
        Assert.Equal("kubernetes", result.Envelope.AdapterId);
        Assert.Equal("apply", result.Envelope.Operation);
        Assert.Contains("dry_run_apply_manifest", toolCaller.CalledTools);
        Assert.Contains("diff_manifest", toolCaller.CalledTools);
    }

    [Fact]
    public async Task BuildAsync_ApplyManifest_EvidenceFailure_ReturnsFailed()
    {
        var toolCaller = new FakeToolCaller()
            .With("dry_run_apply_manifest", "Server-side dry-run failed: connection refused")
            .With("diff_manifest", DiffJson([]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "apply_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("dry-run failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_ApplyManifest_PolicyBlocked_ReturnsFailed()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_apply_manifest", ApplyEvidenceBlockedJson(dryRun, "[PRIVILEGED_CONTAINER] Privileged container detected (apps/v1 Deployment demo/nginx)"));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "apply_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("policy", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_DeleteManifest_HappyPath_ReturnsPlanWithEnvelope()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_delete_manifest", DryRunJson(dryRun))
            .With("diff_manifest", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "delete_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Envelope);
        Assert.Equal("delete", result.Envelope.Operation);
    }

    [Fact]
    public async Task BuildAsync_DeleteManifest_EvidenceFailure_ReturnsFailed()
    {
        var toolCaller = new FakeToolCaller()
            .With("dry_run_delete_manifest", "Dry-run failed: object not found");
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "delete_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("dry-run failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_ScaleDeployment_HappyPath_ReturnsPlanWithEnvelope()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_scale_deployment", DryRunJson(dryRun))
            .With("diff_deployment", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "scale_deployment",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["name"] = "nginx", ["replicas"] = 3 },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("scale", result.Envelope!.Operation);
    }

    [Fact]
    public async Task BuildAsync_RestartDeployment_HappyPath_ReturnsPlanWithEnvelope()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_restart_deployment", DryRunJson(dryRun))
            .With("diff_deployment", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "restart_deployment",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["name"] = "nginx" },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("restart", result.Envelope!.Operation);
    }

    [Fact]
    public async Task BuildAsync_SetDeploymentImage_HappyPath_ReturnsPlanWithEnvelope()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_set_deployment_image", DryRunJson(dryRun))
            .With("diff_deployment", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "set_deployment_image",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["container"] = "nginx",
                ["image"] = "nginx:1.25"
            },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("set-image", result.Envelope!.Operation);
    }

    [Theory]
    [InlineData("nginx")]
    [InlineData("nginx:latest")]
    public async Task BuildAsync_SetDeploymentImage_LatestImageTag_ReturnsPolicyFailureWithoutDryRun(string image)
    {
        var toolCaller = new FakeToolCaller();
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "set_deployment_image",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["container"] = "nginx",
                ["image"] = image
            },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(KubernetesAdapterConventions.PolicyCodes.ImageLatestTag, result.Message);
        Assert.Empty(toolCaller.CalledTools);
    }

    [Fact]
    public async Task BuildAsync_UnsupportedTool_ReturnsFailed()
    {
        var toolCaller = new FakeToolCaller();
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "unknown_tool",
            new Dictionary<string, object?>(),
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Unsupported", result.Message);
    }

    [Fact]
    public async Task BuildAsync_ApplyManifest_FreshnessPolicyIncludesBothChecks()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_apply_manifest", ApplyEvidenceJson(dryRun))
            .With("diff_manifest", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "apply_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Envelope);
        Assert.Equal(2, result.Envelope.FreshnessPolicy.Checks.Count);
        Assert.Contains(result.Envelope.FreshnessPolicy.Checks, c => c.Type == "kubernetes.live-drift");
        Assert.Contains(result.Envelope.FreshnessPolicy.Checks, c => c.Type == "kubernetes.pre-execute-dry-run");
    }

    [Fact]
    public async Task BuildAsync_ScaleDeployment_FreshnessPolicyIncludesOnlyPreExecuteDryRun()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_scale_deployment", DryRunJson(dryRun))
            .With("diff_deployment", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "scale_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["replicas"] = 3
            },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.DoesNotContain(result.Envelope!.FreshnessPolicy.Checks, c => c.Type == "kubernetes.live-drift");
        Assert.Contains(result.Envelope.FreshnessPolicy.Checks, c => c.Type == "kubernetes.pre-execute-dry-run");
    }

    [Fact]
    public async Task BuildAsync_ApplyManifest_MissingNamespace_ReturnsFailed()
    {
        var builder = new KubernetesPlanBuilder(new FakeToolCaller());

        var result = await builder.BuildAsync(
            "apply_manifest",
            new Dictionary<string, object?> { ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Missing required arguments", result.Message);
    }

    [Fact]
    public async Task BuildAsync_DeleteManifest_MissingManifest_ReturnsFailed()
    {
        var builder = new KubernetesPlanBuilder(new FakeToolCaller());

        var result = await builder.BuildAsync(
            "delete_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo" },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Missing required arguments", result.Message);
    }

    [Fact]
    public async Task BuildAsync_ScaleDeployment_MissingName_ReturnsFailed()
    {
        var builder = new KubernetesPlanBuilder(new FakeToolCaller());

        var result = await builder.BuildAsync(
            "scale_deployment",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["replicas"] = 3 },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Missing required arguments", result.Message);
    }

    [Fact]
    public async Task BuildAsync_RestartDeployment_MissingName_ReturnsFailed()
    {
        var builder = new KubernetesPlanBuilder(new FakeToolCaller());

        var result = await builder.BuildAsync(
            "restart_deployment",
            new Dictionary<string, object?> { ["namespace"] = "demo" },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Missing required arguments", result.Message);
    }

    [Fact]
    public async Task BuildAsync_SetDeploymentImage_MissingContainer_ReturnsFailed()
    {
        var builder = new KubernetesPlanBuilder(new FakeToolCaller());

        var result = await builder.BuildAsync(
            "set_deployment_image",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["image"] = "nginx:1.25"
            },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Missing required arguments", result.Message);
    }

    [Fact]
    public async Task BuildAsync_ApplyManifest_WithJsonElementNamespace_Succeeds()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_apply_manifest", ApplyEvidenceJson(dryRun))
            .With("diff_manifest", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var namespaceElement = JsonSerializer.SerializeToElement("demo");
        var manifestElement = JsonSerializer.SerializeToElement("apiVersion: apps/v1");

        var result = await builder.BuildAsync(
            "apply_manifest",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceElement,
                ["manifest"] = manifestElement
            },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Envelope);
        Assert.Equal("apply", result.Envelope.Operation);
    }

    [Fact]
    public async Task BuildAsync_ScaleDeployment_WithLongReplicas_Succeeds()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_scale_deployment", DryRunJson(dryRun))
            .With("diff_deployment", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "scale_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["replicas"] = 3L
            },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task BuildAsync_ScaleDeployment_WithDoubleReplicas_Succeeds()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_scale_deployment", DryRunJson(dryRun))
            .With("diff_deployment", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "scale_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["replicas"] = 3.0d
            },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task BuildAsync_ScaleDeployment_WithStringReplicas_Succeeds()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_scale_deployment", DryRunJson(dryRun))
            .With("diff_deployment", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "scale_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["replicas"] = "3"
            },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task BuildAsync_ScaleDeployment_WithJsonElementNumberReplicas_Succeeds()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_scale_deployment", DryRunJson(dryRun))
            .With("diff_deployment", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var replicasElement = JsonSerializer.SerializeToElement(3);

        var result = await builder.BuildAsync(
            "scale_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["replicas"] = replicasElement
            },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task BuildAsync_ScaleDeployment_WithJsonElementStringReplicas_Succeeds()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var diff = MakeDiff("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_scale_deployment", DryRunJson(dryRun))
            .With("diff_deployment", DiffJson([diff]));
        var builder = new KubernetesPlanBuilder(toolCaller);

        var replicasElement = JsonSerializer.SerializeToElement("3");

        var result = await builder.BuildAsync(
            "scale_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["replicas"] = replicasElement
            },
            TestRequester,
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task BuildAsync_ApplyManifest_EvidenceDeserializedAsNull_ReturnsFailed()
    {
        var toolCaller = new FakeToolCaller()
            .With("dry_run_apply_manifest", "null");
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "apply_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("empty result", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Audit);
        Assert.Equal(ApprovalConventions.AuditEvents.DiffFailed, result.Audit.EventName);
    }

    [Fact]
    public async Task BuildAsync_ApplyManifest_DiffsDeserializedAsNull_ReturnsFailed()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_apply_manifest", ApplyEvidenceJson(dryRun))
            .With("diff_manifest", "null");
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "apply_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("empty result", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Audit);
        Assert.Equal(ApprovalConventions.AuditEvents.DiffFailed, result.Audit.EventName);
    }

    [Fact]
    public async Task BuildAsync_DeleteManifest_DryRunDeserializedAsNull_ReturnsFailed()
    {
        var toolCaller = new FakeToolCaller()
            .With("dry_run_delete_manifest", "null");
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "delete_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("dry-run failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_DeleteManifest_DiffsDeserializedAsNull_ReturnsFailed()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_delete_manifest", DryRunJson(dryRun))
            .With("diff_manifest", "null");
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "delete_manifest",
            new Dictionary<string, object?> { ["namespace"] = "demo", ["manifest"] = "apiVersion: apps/v1" },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("empty result", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_ScaleDeployment_DryRunDeserializedAsNull_ReturnsFailed()
    {
        var toolCaller = new FakeToolCaller()
            .With("dry_run_scale_deployment", "null");
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "scale_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["replicas"] = 3
            },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("dry-run failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_ScaleDeployment_DiffsDeserializedAsNull_ReturnsFailed()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var toolCaller = new FakeToolCaller()
            .With("dry_run_scale_deployment", DryRunJson(dryRun))
            .With("diff_deployment", "null");
        var builder = new KubernetesPlanBuilder(toolCaller);

        var result = await builder.BuildAsync(
            "scale_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = "demo",
                ["name"] = "nginx",
                ["replicas"] = 3
            },
            TestRequester,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Diff evidence failed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Audit);
        Assert.Equal(ApprovalConventions.AuditEvents.DiffFailed, result.Audit.EventName);
    }

    private sealed class FakeToolCaller : IToolCaller
    {
        private readonly Dictionary<string, string> responses = new(StringComparer.Ordinal);
        public List<string> CalledTools { get; } = [];

        public FakeToolCaller With(string toolName, string response)
        {
            responses[toolName] = response;
            return this;
        }

        public Task<string> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
        {
            CalledTools.Add(toolName);
            return Task.FromResult(responses.TryGetValue(toolName, out var response) ? response : string.Empty);
        }
    }
}
