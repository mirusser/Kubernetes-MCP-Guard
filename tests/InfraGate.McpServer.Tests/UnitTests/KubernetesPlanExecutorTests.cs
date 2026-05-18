using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesPlanExecutorTests
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

    private static string ApplyEvidenceJson(K8sPlanDryRun dryRun) =>
        JsonSerializer.Serialize(new K8sApplyEvidence(dryRun, [], false, null), JsonOptions);

    private static PlanEnvelope BuildApplyEnvelope(K8sPlanDiff[] diffs, string ns = "demo", string name = "nginx")
    {
        var payload = new KubernetesPlanPayload(
            ns,
            $"Apply deployment {name}.",
            new Dictionary<string, string> { [KubernetesAdapterConventions.PlanParameters.ObjectCount] = "1" },
            [new K8sObjectRef("apps/v1", "Deployment", ns, name)])
        {
            Manifest = "apiVersion: apps/v1",
            DryRun = MakeDryRun(ns, name),
            Diffs = diffs
        };

        var freshnessPolicy = new FreshnessPolicy(
        [
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.LiveDrift, new Dictionary<string, string>()),
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>())
        ]);

        return KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                ApprovalStore.NewPlanId(),
                KubernetesAdapterConventions.PlanOperations.Apply,
                DateTimeOffset.UtcNow,
                TestRequester,
                payload,
                freshnessPolicy: freshnessPolicy));
    }

    private static PlanEnvelope BuildScaleEnvelope(string ns = "demo", string name = "nginx", int replicas = 3)
    {
        var payload = new KubernetesPlanPayload(
            ns,
            $"Scale deployment {name} to {replicas}.",
            new Dictionary<string, string>
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = name,
                [KubernetesAdapterConventions.PlanParameters.Replicas] = replicas.ToString()
            },
            [new K8sObjectRef("apps/v1", "Deployment", ns, name)])
        {
            DryRun = MakeDryRun(ns, name)
        };

        var freshnessPolicy = new FreshnessPolicy(
        [
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.LiveDrift, new Dictionary<string, string>()),
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>())
        ]);

        return KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                ApprovalStore.NewPlanId(),
                KubernetesAdapterConventions.PlanOperations.Scale,
                DateTimeOffset.UtcNow,
                TestRequester,
                payload,
                freshnessPolicy: freshnessPolicy));
    }

    [Fact]
    public async Task ExecuteAsync_ApplyManifest_HappyPath_DispatchesToApplyTool()
    {
        var diff = MakeDiff("demo", "nginx");
        var dryRun = MakeDryRun("demo", "nginx");
        var envelope = BuildApplyEnvelope([diff]);

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest, ApplyEvidenceJson(dryRun))
            .With(KubernetesAdapterConventions.MutationTools.ApplyManifest, "Applied successfully.");

        var executor = new KubernetesPlanExecutor(toolCaller);
        var result = await executor.ExecuteAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal("Applied successfully.", result.Message);
        Assert.Contains(KubernetesAdapterConventions.MutationTools.ApplyManifest, toolCaller.CalledTools);
    }

    [Fact]
    public async Task ExecuteAsync_ScaleDeployment_HappyPath_DispatchesToScaleTool()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var envelope = BuildScaleEnvelope();

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunScaleDeployment, DryRunJson(dryRun))
            .With(KubernetesAdapterConventions.MutationTools.ScaleDeployment, "Scaled to 3 replicas.");

        var executor = new KubernetesPlanExecutor(toolCaller);
        var result = await executor.ExecuteAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal("Scaled to 3 replicas.", result.Message);
        Assert.Contains(KubernetesAdapterConventions.MutationTools.ScaleDeployment, toolCaller.CalledTools);
    }

    [Fact]
    public async Task CheckPreExecutionAsync_DriftDetected_BlocksExecution()
    {
        var diff = MakeDiff("demo", "nginx");
        var envelope = BuildApplyEnvelope([diff]);

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, "Replica count changed from 2 to 4.");

        var executor = new KubernetesPlanExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("drift", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Audit);
        Assert.Equal(ApprovalConventions.AuditEvents.ApplyDriftDetected, result.Audit.EventName);
        Assert.DoesNotContain(KubernetesAdapterConventions.MutationTools.ApplyManifest, toolCaller.CalledTools);
    }

    [Fact]
    public async Task CheckPreExecutionAsync_PreExecuteDryRunFails_BlocksExecution()
    {
        var diff = MakeDiff("demo", "nginx");
        var envelope = BuildApplyEnvelope([diff]);

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest, "Server-side dry-run failed: webhook rejected");

        var executor = new KubernetesPlanExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("dry-run", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Audit);
        Assert.Equal(ApprovalConventions.AuditEvents.DryRunFailed, result.Audit.EventName);
        Assert.DoesNotContain(KubernetesAdapterConventions.MutationTools.ApplyManifest, toolCaller.CalledTools);
    }

    [Fact]
    public async Task CheckPreExecutionAsync_PolicyBlockedAtExecution_BlocksExecution()
    {
        var diff = MakeDiff("demo", "nginx");
        var envelope = BuildApplyEnvelope([diff]);
        var dryRun = MakeDryRun("demo", "nginx");

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
                JsonSerializer.Serialize(
                    new K8sApplyEvidence(dryRun, [], true, "[PRIVILEGED_CONTAINER] Privileged container detected"),
                    JsonOptions));

        var executor = new KubernetesPlanExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("policy", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(KubernetesAdapterConventions.MutationTools.ApplyManifest, toolCaller.CalledTools);
    }

    [Fact]
    public async Task ExecuteAsync_DecodeFailure_ReturnsError()
    {
        var badEnvelope = BuildApplyEnvelope([]) with { AdapterId = "unknown-adapter" };

        var toolCaller = new FakeToolCaller();
        var executor = new KubernetesPlanExecutor(toolCaller);
        var result = await executor.ExecuteAsync(badEnvelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("unsupported adapter", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(toolCaller.CalledTools);
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
