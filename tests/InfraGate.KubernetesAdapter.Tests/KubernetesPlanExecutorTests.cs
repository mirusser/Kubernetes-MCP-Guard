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

    public static TheoryData<string, Dictionary<string, string>, string> CorruptedScalePayloadCases { get; } =
        new()
        {
            {
                string.Empty,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [KubernetesAdapterConventions.PlanParameters.Name] = "nginx",
                    [KubernetesAdapterConventions.PlanParameters.Replicas] = "3"
                },
                "Namespace"
            },
            {
                "demo",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [KubernetesAdapterConventions.PlanParameters.Replicas] = "3"
                },
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Name}\"]"
            },
            {
                "demo",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [KubernetesAdapterConventions.PlanParameters.Name] = "nginx"
                },
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Replicas}\"]"
            }
        };

    private static KubernetesPlanDryRun MakeDryRun(string ns, string name) =>
        new(
            "succeeded",
            DateTimeOffset.UtcNow,
            [new KubernetesPlanDryRunObject($"apps/v1 Deployment {ns}/{name}", "{}")],
            [],
            "Server-side dry-run succeeded.");

    private static KubernetesPlanDiff MakeDiff(string ns, string name, string? resourceVersion = null) =>
        new(
            new KubernetesObjectRef("apps/v1", "Deployment", ns, name),
            "update",
            $"Update apps/v1 Deployment {ns}/{name}",
            "@@ -1 +1 @@",
            "{}",
            "{}",
            [],
            [],
            [],
            resourceVersion);

    private static string DryRunJson(KubernetesPlanDryRun dryRun) =>
        JsonSerializer.Serialize(dryRun, JsonOptions);

    private static string ApplyEvidenceJson(KubernetesPlanDryRun dryRun) =>
        JsonSerializer.Serialize(new KubernetesApplyEvidence(dryRun, [], false, null), JsonOptions);

    private static KubernetesPlanExecutor CreateExecutor(
        FakeToolCaller toolCaller,
        IApprovalAuditOutbox? auditOutbox = null) =>
        new(toolCaller, new KubernetesEvidenceService(toolCaller), auditOutbox);

    private static PlanEnvelope BuildApplyEnvelope(
        KubernetesPlanDiff[] diffs,
        string ns = "demo",
        string name = "nginx",
        KubernetesPlanPolicyFinding[]? policyFindings = null,
        FreshnessPolicy? freshnessPolicy = null)
    {
        var payload = new KubernetesPlanPayload(
            ns,
            $"Apply deployment {name}.",
            new Dictionary<string, string> { [KubernetesAdapterConventions.PlanParameters.ObjectCount] = "1" },
            [new KubernetesObjectRef("apps/v1", "Deployment", ns, name)])
        {
            Manifest = "apiVersion: apps/v1",
            DryRun = MakeDryRun(ns, name),
            Diffs = diffs,
            PolicyFindings = policyFindings ?? []
        };

        freshnessPolicy ??= new FreshnessPolicy(
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

    private static PlanEnvelope BuildRestartEnvelope(string ns = "demo", string name = "nginx")
    {
        var payload = new KubernetesPlanPayload(
            ns,
            $"Restart deployment {name}.",
            new Dictionary<string, string> { [KubernetesAdapterConventions.PlanParameters.Name] = name },
            [new KubernetesObjectRef("apps/v1", "Deployment", ns, name)])
        {
            DryRun = MakeDryRun(ns, name),
            Diffs = [MakeDiff(ns, name)]
        };

        var freshnessPolicy = new FreshnessPolicy(
        [
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.LiveDrift, new Dictionary<string, string>()),
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>())
        ]);

        return KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                ApprovalIds.NewPlanId(),
                KubernetesAdapterConventions.PlanOperations.Restart,
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

    private static KubernetesPlanPolicyFinding MakeDenyFinding(string ns = "demo", string name = "nginx") =>
        new(
            KubernetesAdapterConventions.PolicySeverities.Deny,
            KubernetesAdapterConventions.PolicyCodes.DeploymentPrivilegedContainer,
            $"Deployment {ns}/{name}",
            "Privileged container detected.");

    [Fact]
    public async Task CheckPreExecutionAsync_ResourceVersionMismatch_BlocksExecution()
    {
        var diff = MakeDiff("demo", "nginx", "12345");
        var dryRun = MakeDryRun("demo", "nginx");
        var freshnessPolicy = new FreshnessPolicy(
        [
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.ResourceVersionCheck,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["apps/v1 Deployment demo/nginx"] = "42"
                }),
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.LiveDrift, new Dictionary<string, string>()),
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>())
        ]);
        var envelope = BuildApplyEnvelope([diff], freshnessPolicy: freshnessPolicy);

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift,
                KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.CheckResourceVersion,
                "Resource version changed for apps/v1 Deployment demo/nginx: expected 42, actual 99.")
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
                ApplyEvidenceJson(dryRun));

        var executor = CreateExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.ResourceVersionMismatch, result.ReasonCode);
        Assert.Contains(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, toolCaller.CalledTools);
        Assert.Contains(KubernetesAdapterConventions.EvidenceTools.CheckResourceVersion, toolCaller.CalledTools);
        Assert.DoesNotContain(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest, toolCaller.CalledTools);
    }

    [Fact]
    public async Task CheckPreExecutionAsync_DriftPasses_ProceedsToRemainingChecks()
    {
        var diff = MakeDiff("demo", "nginx");
        var dryRun = MakeDryRun("demo", "nginx");
        var freshnessPolicy = new FreshnessPolicy(
        [
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.ResourceVersionCheck,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["apps/v1 Deployment demo/nginx"] = "42"
                }),
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.LiveDrift, new Dictionary<string, string>()),
            new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>())
        ]);
        var envelope = BuildApplyEnvelope([diff], freshnessPolicy: freshnessPolicy);

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift,
                KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.CheckResourceVersion,
                KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
                ApplyEvidenceJson(dryRun));

        var executor = CreateExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Contains(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, toolCaller.CalledTools);
        Assert.Contains(KubernetesAdapterConventions.EvidenceTools.CheckResourceVersion, toolCaller.CalledTools);
        Assert.Contains(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest, toolCaller.CalledTools);
    }

    [Fact]
    public async Task CheckPreExecutionAsync_NoResourceVersionCheck_ProceedsNormally()
    {
        var diff = MakeDiff("demo", "nginx");
        var dryRun = MakeDryRun("demo", "nginx");
        var envelope = BuildApplyEnvelope([diff]);

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift,
                KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
                ApplyEvidenceJson(dryRun));

        var executor = CreateExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.DoesNotContain(KubernetesAdapterConventions.EvidenceTools.CheckResourceVersion, toolCaller.CalledTools);
    }

    [Fact]
    public async Task ExecuteAsync_ApplyManifest_HappyPath_DispatchesToApplyTool()
    {
        var diff = MakeDiff("demo", "nginx", "12345");
        var dryRun = MakeDryRun("demo", "nginx");
        var envelope = BuildApplyEnvelope([diff]);

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest, ApplyEvidenceJson(dryRun))
            .With(KubernetesAdapterConventions.MutationTools.ApplyManifest, "Applied successfully.");

        var executor = CreateExecutor(toolCaller);
        var result = await executor.ExecuteAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal("Applied successfully.", result.Message);
        Assert.Contains(KubernetesAdapterConventions.MutationTools.ApplyManifest, toolCaller.CalledTools);
        var applyArguments = toolCaller.ArgumentsByTool[KubernetesAdapterConventions.MutationTools.ApplyManifest];
        var resourceVersionsJson = Assert.IsType<string>(
            applyArguments[KubernetesAdapterConventions.EvidenceArguments.ResourceVersions]);
        using var resourceVersions = JsonDocument.Parse(resourceVersionsJson);
        var resourceVersion = resourceVersions.RootElement[0];
        Assert.Equal("apps/v1 Deployment demo/nginx", resourceVersion.GetProperty("key").GetString());
        Assert.Equal("12345", resourceVersion.GetProperty("resourceVersion").GetString());
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

        var executor = CreateExecutor(toolCaller);
        var result = await executor.ExecuteAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal("Scaled to 3 replicas.", result.Message);
        Assert.Contains(KubernetesAdapterConventions.MutationTools.ScaleDeployment, toolCaller.CalledTools);
    }

    [Theory]
    [MemberData(nameof(CorruptedScalePayloadCases))]
    public async Task ExecuteAsync_ScaleDeployment_CorruptedStoredPayload_ThrowsBeforeMutation(
        string namespaceName,
        Dictionary<string, string> parameters,
        string expectedParameterName)
    {
        var envelope = BuildScaleEnvelope();
        var decoded = KubernetesApprovalAdapter.Decode(envelope);
        Assert.True(decoded.Succeeded, decoded.Message);
        Assert.NotNull(decoded.Plan);
        var corruptedPayload = decoded.Plan.Payload with
        {
            Namespace = namespaceName,
            Parameters = parameters
        };
        var corruptedEnvelope = KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                decoded.Plan.Id,
                decoded.Plan.Operation,
                decoded.Plan.CreatedAtUtc,
                decoded.Plan.Requester,
                corruptedPayload,
                decoded.Plan.Envelope.ReviewSurfaceContext,
                decoded.Plan.Envelope.FreshnessPolicy,
                decoded.Plan.Envelope.ApprovalPolicy));
        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.MutationTools.ScaleDeployment, "Scaled to 3 replicas.");
        var executor = CreateExecutor(toolCaller);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            executor.ExecuteAsync(corruptedEnvelope, CancellationToken.None));

        Assert.Equal(expectedParameterName, exception.ParamName);
        Assert.DoesNotContain(KubernetesAdapterConventions.MutationTools.ScaleDeployment, toolCaller.CalledTools);
    }

    [Fact]
    public async Task ExecuteAsync_RestartDeployment_HappyPath_DispatchesToRestartTool()
    {
        var envelope = BuildRestartEnvelope();
        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.MutationTools.RestartDeployment, "Restarted successfully.");

        var executor = CreateExecutor(toolCaller);
        var result = await executor.ExecuteAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal("Restarted successfully.", result.Message);
        Assert.Contains(KubernetesAdapterConventions.MutationTools.RestartDeployment, toolCaller.CalledTools);
    }

    [Fact]
    public async Task ExecuteAsync_ScaleDeployment_PublishesExecutionStartedBeforeMutation()
    {
        var envelope = BuildScaleEnvelope();
        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.MutationTools.ScaleDeployment, "Scaled to 3 replicas.");
        var outbox = new RecordingApprovalAuditOutbox(() => toolCaller.CalledTools.ToArray());

        var executor = CreateExecutor(toolCaller, outbox);
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

        var executor = CreateExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.LiveDrift, result.ReasonCode);
        Assert.NotNull(result.Audit);
        Assert.Equal(ApprovalConventions.AuditEvents.ApplyDriftDetected, result.Audit.EventName);
        Assert.DoesNotContain(KubernetesAdapterConventions.MutationTools.ApplyManifest, toolCaller.CalledTools);
    }

    [Fact]
    public async Task CheckPreExecutionAsync_LiveDriftNotInFreshnessPolicy_SkipsDriftCheck()
    {
        var diff = MakeDiff("demo", "nginx");
        var dryRun = MakeDryRun("demo", "nginx");
        var payload = new KubernetesPlanPayload(
            "demo",
            "Restart deployment nginx.",
            new Dictionary<string, string>
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = "nginx"
            },
            [new KubernetesObjectRef("apps/v1", "Deployment", "demo", "nginx")])
        {
            DryRun = dryRun,
            Diffs = [diff]
        };

        var envelope = KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                ApprovalIds.NewPlanId(),
                KubernetesAdapterConventions.PlanOperations.Restart,
                DateTimeOffset.UtcNow,
                TestRequester,
                payload,
                freshnessPolicy: new FreshnessPolicy(
                [
                    new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>())
                ])));

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunRestartDeployment, DryRunJson(dryRun));

        var executor = CreateExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.DoesNotContain(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, toolCaller.CalledTools);
        Assert.Contains(KubernetesAdapterConventions.EvidenceTools.DryRunRestartDeployment, toolCaller.CalledTools);
    }

    [Fact]
    public async Task CheckPreExecutionAsync_PreExecuteDryRunFails_BlocksExecution()
    {
        var diff = MakeDiff("demo", "nginx");
        var envelope = BuildApplyEnvelope([diff]);

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest, "Server-side dry-run failed: webhook rejected");

        var executor = CreateExecutor(toolCaller);
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
        var envelope = BuildApplyEnvelope([diff], policyFindings: [MakeDenyFinding()]);
        var dryRun = MakeDryRun("demo", "nginx");

        var toolCaller = new FakeToolCaller()
            .With(KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift, KubernetesAdapterConventions.DriftCheckResults.NoDrift)
            .With(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
                JsonSerializer.Serialize(
                    new KubernetesApplyEvidence(dryRun, [], true, "[PRIVILEGED_CONTAINER] Privileged container detected"),
                    JsonOptions));

        var executor = CreateExecutor(toolCaller);
        var result = await executor.CheckPreExecutionAsync(envelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked, result.ReasonCode);
        Assert.DoesNotContain(KubernetesAdapterConventions.MutationTools.ApplyManifest, toolCaller.CalledTools);
        Assert.DoesNotContain(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest, toolCaller.CalledTools);
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

        var executor = CreateExecutor(toolCaller);
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

        var executor = CreateExecutor(toolCaller);
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

        var executor = CreateExecutor(toolCaller, outbox);
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
        var executor = CreateExecutor(toolCaller);
        var result = await executor.ExecuteAsync(badEnvelope, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.UnsupportedAdapter, result.ReasonCode);
        Assert.Empty(toolCaller.CalledTools);
    }

    private sealed class FakeToolCaller : IToolCaller
    {
        private readonly Dictionary<string, string> responses = new(StringComparer.Ordinal);
        public List<string> CalledTools { get; } = [];
        public Dictionary<string, IReadOnlyDictionary<string, object?>> ArgumentsByTool { get; } =
            new(StringComparer.Ordinal);

        public FakeToolCaller With(string toolName, string response)
        {
            responses[toolName] = response;
            return this;
        }

        public Task<string> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
        {
            CalledTools.Add(toolName);
            ArgumentsByTool[toolName] = new Dictionary<string, object?>(arguments, StringComparer.Ordinal);
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
