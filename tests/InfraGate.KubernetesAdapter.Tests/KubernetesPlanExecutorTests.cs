using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.AuditPayloads;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.Approval;
using InfraGate.KubernetesAdapter.Evidence;
using InfraGate.KubernetesAdapter.Execution;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Tests.UnitTests;

public sealed class KubernetesPlanExecutorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PlanRequester TestRequester = new("test-subject", "oauth-jwt");

    private static KubernetesPlanDryRun MakeDryRun(string ns, string name) =>
        new(
            "succeeded",
            DateTimeOffset.UtcNow,
            [new KubernetesPlanDryRunObject($"apps/v1 Deployment {ns}/{name}", "{}")],
            [],
            "Server-side dry-run succeeded.");

    private static KubernetesPlanDiff MakeDiff(string ns, string name) =>
        new(
            new KubernetesObjectRef("apps/v1", "Deployment", ns, name),
            "update",
            $"Update apps/v1 Deployment {ns}/{name}",
            "@@ -1 +1 @@",
            "{}",
            "{}",
            [],
            [],
            []);

    private static string DryRunJson(KubernetesPlanDryRun dryRun) =>
        JsonSerializer.Serialize(dryRun, JsonOptions);

    private static string ApplyEvidenceJson(KubernetesPlanDryRun dryRun) =>
        JsonSerializer.Serialize(new KubernetesApplyEvidence(dryRun, [], false, null), JsonOptions);

    private static PlanEnvelope BuildApplyEnvelope(KubernetesPlanDiff[] diffs, string ns = "demo", string name = "nginx")
    {
        var payload = new KubernetesPlanPayload(
            ns,
            $"Apply deployment {name}.",
            new Dictionary<string, string> { [KubernetesAdapterConventions.PlanParameters.ObjectCount] = "1" },
            [new KubernetesObjectRef("apps/v1", "Deployment", ns, name)])
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
                ApprovalIds.NewPlanId(),
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
            [new KubernetesObjectRef("apps/v1", "Deployment", ns, name)])
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
                ApprovalIds.NewPlanId(),
                KubernetesAdapterConventions.PlanOperations.Scale,
                DateTimeOffset.UtcNow,
                TestRequester,
                payload,
                freshnessPolicy: freshnessPolicy));
    }

    private static PlanEnvelope BuildSetImageEnvelope(string image, string ns = "demo", string name = "nginx")
    {
        var payload = new KubernetesPlanPayload(
            ns,
            $"Update deployment {name} image.",
            new Dictionary<string, string>
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = name,
                [KubernetesAdapterConventions.PlanParameters.Container] = "nginx",
                [KubernetesAdapterConventions.PlanParameters.Image] = image
            },
            [new KubernetesObjectRef("apps/v1", "Deployment", ns, name)])
        {
            DryRun = MakeDryRun(ns, name)
        };

        var freshnessPolicy = new FreshnessPolicy(
        [
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>())
        ]);

        return KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                ApprovalIds.NewPlanId(),
                KubernetesAdapterConventions.PlanOperations.SetImage,
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
    public async Task ExecuteAsync_ScaleDeployment_PublishesExecutionStartedBeforeMutation()
    {
        var envelope = BuildScaleEnvelope();
        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.MutationTools.ScaleDeployment, "Scaled to 3 replicas.");
        var outbox = new RecordingApprovalAuditOutbox(() => toolCaller.CalledTools.ToArray());

        var executor = new KubernetesPlanExecutor(toolCaller, outbox);
        var result = await executor.ExecuteAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Contains(KubernetesAdapterConventions.MutationTools.ScaleDeployment, toolCaller.CalledTools);
        var audit = Assert.Single(outbox.Events);
        Assert.Equal(ApprovalConventions.AuditEvents.ExecutionStarted, audit.EventName);
        var payload = Assert.IsType<ExecutionStartedPayload>(audit.Payload);
        Assert.Equal(envelope.Id, payload.PlanId);
        Assert.Equal(KubernetesAdapterConventions.PlanOperations.Scale, payload.Operation);
        Assert.Equal(KubernetesAdapterConventions.AdapterId, payload.AdapterId);
        Assert.Equal("demo", payload.AdapterPayload.GetProperty("namespaceName").GetString());
        Assert.DoesNotContain(KubernetesAdapterConventions.MutationTools.ScaleDeployment, outbox.CalledToolsAtPublish);
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
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.LiveDrift, result.ReasonCode);
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
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.PreExecuteDryRunFailed, result.ReasonCode);
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
                    new KubernetesApplyEvidence(dryRun, [], true, "[PRIVILEGED_CONTAINER] Privileged container detected"),
                    JsonOptions));

        var executor = new KubernetesPlanExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked, result.ReasonCode);
        Assert.DoesNotContain(KubernetesAdapterConventions.MutationTools.ApplyManifest, toolCaller.CalledTools);
    }

    [Theory]
    [InlineData("nginx")]
    [InlineData("nginx:latest")]
    public async Task CheckPreExecutionAsync_SetDeploymentImageLatestImageTag_BlocksWithoutDryRun(string image)
    {
        var envelope = BuildSetImageEnvelope(image);
        var dryRun = MakeDryRun("demo", "nginx");

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage, DryRunJson(dryRun));

        var executor = new KubernetesPlanExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains(KubernetesAdapterConventions.PolicyCodes.ImageLatestTag, result.Message);
        Assert.NotNull(result.Audit);
        Assert.Equal(ApprovalConventions.AuditEvents.ApplyDenied, result.Audit.EventName);
        Assert.DoesNotContain(KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage, toolCaller.CalledTools);
    }

    [Fact]
    public async Task CheckPreExecutionAsync_SetDeploymentImagePinnedImageTag_RunsDryRun()
    {
        var envelope = BuildSetImageEnvelope("nginx:1.25");
        var dryRun = MakeDryRun("demo", "nginx");

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage, DryRunJson(dryRun));

        var executor = new KubernetesPlanExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Contains(KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage, toolCaller.CalledTools);
    }

    [Fact]
    public async Task CheckPreExecutionAsync_PassingChecks_PublishesPreExecutionCheckedAudit()
    {
        var dryRun = MakeDryRun("demo", "nginx");
        var envelope = BuildScaleEnvelope();
        var outbox = new RecordingApprovalAuditOutbox();

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunScaleDeployment, DryRunJson(dryRun));

        var executor = new KubernetesPlanExecutor(toolCaller, outbox);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        var audit = Assert.Single(outbox.Events);
        Assert.Equal(ApprovalConventions.AuditEvents.PreExecutionChecked, audit.EventName);
        var payload = Assert.IsType<PreExecutionCheckedPayload>(audit.Payload);
        Assert.Equal(envelope.Id, payload.PlanId);
        Assert.Equal(KubernetesAdapterConventions.PlanOperations.Scale, payload.Operation);
        Assert.Equal(KubernetesAdapterConventions.AdapterId, payload.AdapterId);
        Assert.Equal("demo", payload.AdapterPayload.GetProperty("namespaceName").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_DecodeFailure_ReturnsError()
    {
        var badEnvelope = BuildApplyEnvelope([]) with { AdapterId = "unknown-adapter" };

        var toolCaller = new FakeToolCaller();
        var executor = new KubernetesPlanExecutor(toolCaller);
        var result = await executor.ExecuteAsync(badEnvelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.UnsupportedAdapter, result.ReasonCode);
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

    private sealed class RecordingApprovalAuditOutbox(Func<string[]>? captureCalledTools = null) : IApprovalAuditOutbox
    {
        public List<ApprovalAuditEntry> Events { get; } = [];

        public string[] CalledToolsAtPublish { get; private set; } = [];

        public Task<long> AppendAsync(ApprovalAuditEntry entry, CancellationToken cancellationToken)
        {
            CalledToolsAtPublish = captureCalledTools?.Invoke() ?? [];
            Events.Add(entry);
            return Task.FromResult((long)Events.Count);
        }
    }
}
