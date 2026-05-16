using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpServer;
using InfraGate.McpServer.Diff;
using k8s;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sManagerApplyTests
{
    private const string DemoNamespace = "demo";
    private const string PlanOperationApply = "apply";
    private const string PlanParameterObjectCount = "objectCount";
    private const string RequesterSubject = "test-requester";
    private const string RequesterAuthenticationType = "test";

    [Fact]
    public async Task ApplyApprovedPlanAsync_PatchesDeploymentScale()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo/scale" => TestResponse.Json(ScaleJson(3)),
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json(DeploymentJson(3)),
            _ => StatusResponse(request, DeploymentJson(3))
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestScaleDeploymentAsync(
            DemoNamespace,
            "demo",
            3,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);
        var patch = Assert.Single(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo/scale" &&
            !IsDryRun(request));
        var dryRuns = api.Requests.Where(request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo/scale" &&
            IsDryRun(request)).ToArray();

        Assert.Contains("Scaled apps/v1 Deployment demo/demo to 3 replicas.", result);
        Assert.Contains("Deployment rollout completed for demo.", result);
        Assert.Contains("fieldManager=infra-gate-mcp", patch.Query);
        Assert.Equal(2, dryRuns.Length);
        Assert.All(dryRuns, request =>
        {
            Assert.Contains("fieldManager=infra-gate-mcp", request.Query);
            Assert.Contains("fieldValidation=Strict", request.Query);
        });

        using var document = JsonDocument.Parse(patch.Body);
        Assert.Equal(3, document.RootElement.GetProperty("spec").GetProperty("replicas").GetInt32());
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_PatchesDeploymentRestartAnnotation()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json(DeploymentJson()),
            _ => StatusResponse(request, DeploymentJson())
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestRestartDeploymentAsync(
            DemoNamespace,
            "demo",
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);
        var patch = Assert.Single(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo" &&
            !IsDryRun(request));

        Assert.Contains("Restarted apps/v1 Deployment demo/demo", result);
        Assert.Contains("Deployment rollout completed for demo.", result);
        Assert.Contains("fieldManager=infra-gate-mcp", patch.Query);

        using var document = JsonDocument.Parse(patch.Body);
        var annotations = document.RootElement
            .GetProperty("spec")
            .GetProperty("template")
            .GetProperty("metadata")
            .GetProperty("annotations");
        var restartedAt = annotations.GetProperty("kubectl.kubernetes.io/restartedAt").GetString();

        Assert.False(string.IsNullOrWhiteSpace(restartedAt));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_TreatsDeleteNotFoundAsAlreadyAbsent()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/api/v1/namespaces/demo/services/demo" when request.Method == "DELETE" =>
                TestResponse.Json(ServiceJson()),
            "/api/v1/namespaces/demo/configmaps/missing-config" when request.Method == "DELETE" && IsDryRun(request) =>
                TestResponse.Json(ServiceJson()),
            "/api/v1/namespaces/demo/configmaps/missing-config" when request.Method == "DELETE" && !IsDryRun(request) =>
                TestResponse.Json(StatusJson("NotFound", 404), statusCode: 404),
            _ => StatusResponse(request, DeploymentJson())
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestDeleteManifestAsync(
            DemoNamespace,
            DeleteManifest,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("Deleted v1 Service demo/demo", result);
        Assert.Contains("Skipped missing v1 ConfigMap demo/missing-config", result);
        Assert.Contains("No rollout wait for delete operations.", result);
        Assert.Contains(api.Requests, request =>
            request.Method == "DELETE" &&
            request.Path == "/api/v1/namespaces/demo/configmaps/missing-config");
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_RefusesApplyManifestObjectMismatch()
    {
        await using var api = new TestKubernetesApi(request => request.Path == "/api/v1/namespaces/demo/services/planned-service"
            ? TestResponse.Json(ServiceJson("planned-service"))
            : TestResponse.Json("{}"));
        var context = CreateManager(api);
        var serviceRef = new K8sObjectRef("v1", "Service", DemoNamespace, "planned-service");
        var plan = CreatePlan(
            "apply-mismatch",
            PlanOperationApply,
            DemoNamespace,
            "Apply mismatched test manifest.",
            new Dictionary<string, string>
            {
                [PlanParameterObjectCount] = "1"
            },
            [serviceRef],
            MismatchedApplyManifest,
            CreateDryRun(serviceRef),
            [CreateDiff(serviceRef, ServiceJson("planned-service"), ServiceJson("planned-service"))]);
        await context.ApprovalStore.CreatePlanAsync(plan, plan.Payload.Namespace, CancellationToken.None);
        await ApprovePlanAsync(context, plan.Id);

        var result = await context.Manager.ApplyApprovedPlanAsync(plan.Id, CancellationToken.None);

        Assert.Contains("Apply plan manifest no longer matches the planned object references.", result);
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_RefusesPlanWithoutRecordedDryRun()
    {
        var context = CreateManager();
        var plan = CreatePlan(
            "legacy-plan",
            K8sConventions.PlanOperations.Scale,
            DemoNamespace,
            "Legacy scale plan.",
            new Dictionary<string, string>
            {
                [K8sConventions.PlanParameters.Name] = "demo",
                [K8sConventions.PlanParameters.Replicas] = "2"
            },
            [K8sConventions.K8sResources.DeploymentRef(DemoNamespace, "demo")]);
        await context.ApprovalStore.CreatePlanAsync(plan, plan.Payload.Namespace, CancellationToken.None);
        await ApprovePlanAsync(context, plan.Id);

        var result = await context.Manager.ApplyApprovedPlanAsync(plan.Id, CancellationToken.None);

        Assert.Contains("missing recorded server-side dry-run data", result);
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_LegacyApprovedHashWithoutGrant_RefusesMutation()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo/scale" => TestResponse.Json(ScaleJson(3)),
            _ => StatusResponse(request, DeploymentJson(3))
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestScaleDeploymentAsync(
            DemoNamespace,
            "demo",
            3,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = ParsePlanId(requestText);
        var hash = await ApprovalStore.ComputeSha256Async(
            context.ApprovalStore.GetPendingPath(planId),
            CancellationToken.None);
        var legacyApprovedPath = LegacyApprovedPath(context.ApprovalStore, planId);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyApprovedPath)!);
        await File.WriteAllTextAsync(legacyApprovedPath, hash, CancellationToken.None);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("not approved", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo/scale" &&
            !IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_WhenPendingReviewDigestChangedAfterGrant_RefusesMutation()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo/scale" => TestResponse.Json(ScaleJson(3)),
            _ => StatusResponse(request, DeploymentJson(3))
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestScaleDeploymentAsync(
            DemoNamespace,
            "demo",
            3,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);
        var pendingPath = context.ApprovalStore.GetPendingPath(planId);
        var json = await File.ReadAllTextAsync(pendingPath, CancellationToken.None);
        await File.WriteAllTextAsync(
            pendingPath,
            json.Replace("Server-side dry-run succeeded.", "Tampered dry-run review.", StringComparison.Ordinal),
            CancellationToken.None);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("review digest", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo/scale" &&
            !IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_WhenGrantExpired_RefusesMutation()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo/scale" => TestResponse.Json(ScaleJson(3)),
            _ => StatusResponse(request, DeploymentJson(3))
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestScaleDeploymentAsync(
            DemoNamespace,
            "demo",
            3,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);
        var grant = await context.ApprovalStore.GetGrantAsync(planId, CancellationToken.None) ??
                    throw new InvalidOperationException("Grant was not written.");
        await File.WriteAllTextAsync(
            context.ApprovalStore.GetGrantPath(planId),
            JsonSerializer.Serialize(grant with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }),
            CancellationToken.None);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("expired", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo/scale" &&
            !IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_RefusesPlanWithoutDiff()
    {
        var context = CreateManager();
        var plan = CreatePlan(
            "legacy-diff-plan",
            K8sConventions.PlanOperations.Scale,
            DemoNamespace,
            "Legacy scale plan without diff.",
            new Dictionary<string, string>
            {
                [K8sConventions.PlanParameters.Name] = "demo",
                [K8sConventions.PlanParameters.Replicas] = "2"
            },
            [K8sConventions.K8sResources.DeploymentRef(DemoNamespace, "demo")],
            manifest: null,
            dryRun: CreateDryRun(K8sConventions.K8sResources.DeploymentRef(DemoNamespace, "demo")),
            diffs: []);
        await context.ApprovalStore.CreatePlanAsync(plan, plan.Payload.Namespace, CancellationToken.None);
        await ApprovePlanAsync(context, plan.Id);

        var result = await context.Manager.ApplyApprovedPlanAsync(plan.Id, CancellationToken.None);

        Assert.Contains("missing recorded diff data", result);
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_PreApplyDryRunFails_RefusesMutation()
    {
        var patchCount = 0;
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo/scale" when request.Method == "PATCH" => ++patchCount <= 1
                ? TestResponse.Json(ScaleJson(3))
                : TestResponse.Json(StatusJson("Invalid", 422), statusCode: 422),
            "/apis/apps/v1/namespaces/demo/deployments/demo/scale" => TestResponse.Json(ScaleJson(3)),
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json(DeploymentJson(3)),
            _ => StatusResponse(request, DeploymentJson(3))
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestScaleDeploymentAsync(
            DemoNamespace,
            "demo",
            3,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("Server-side dry-run failed immediately before apply", result);
        Assert.DoesNotContain(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo/scale" &&
            !IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_PolicyRevalidatedAtApplyTime_RejectsTamperedManifest()
    {
        await using var api = new TestKubernetesApi(request => request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo"
            ? TestResponse.Json(DeploymentJson())
            : TestResponse.Json("{}"));
        var context = CreateManager(api);
        var deploymentRef = new K8sObjectRef("apps/v1", "Deployment", DemoNamespace, "demo");
        var plan = CreatePlan(
            "policy-revalidate",
            PlanOperationApply,
            DemoNamespace,
            "Apply tampered manifest.",
            new Dictionary<string, string> { [PlanParameterObjectCount] = "1" },
            [deploymentRef],
            PrivilegedDeploymentManifest,
            CreateDryRun(deploymentRef),
            [CreateDiff(deploymentRef, DeploymentJson(), DeploymentJson())]);
        await context.ApprovalStore.CreatePlanAsync(plan, plan.Payload.Namespace, CancellationToken.None);
        await ApprovePlanAsync(context, plan.Id);

        var result = await context.Manager.ApplyApprovedPlanAsync(plan.Id, CancellationToken.None);

        Assert.Contains("re-validated at apply time", result);
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_WhenLiveObjectChangedAfterApproval_RefusesMutation()
    {
        var liveReplicas = 1;
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo/scale" when request.Method == "PATCH" =>
                TestResponse.Json(ScaleJson(3)),
            "/apis/apps/v1/namespaces/demo/deployments/demo/scale" =>
                TestResponse.Json(ScaleJson(liveReplicas)),
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json(DeploymentJson(3)),
            _ => StatusResponse(request, DeploymentJson(3))
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestScaleDeploymentAsync(
            DemoNamespace,
            "demo",
            3,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);
        liveReplicas = 2;

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("Live Kubernetes state changed after approval", result);
        Assert.DoesNotContain(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo/scale" &&
            !IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_WhenCreateTargetAppearsAfterApproval_RefusesMutation()
    {
        var exists = false;
        await using var api = new TestKubernetesApi(request => request.Method switch
        {
            "PATCH" => TestResponse.Json(ConfigMapJson("new")),
            "GET" when exists => TestResponse.Json(ConfigMapJson("old")),
            "GET" => TestResponse.Json(StatusJson("NotFound", 404), statusCode: 404),
            _ => TestResponse.Json("{}")
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestApplyManifestAsync(
            DemoNamespace,
            ConfigMapManifest,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);
        exists = true;

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("Live Kubernetes state changed after approval", result);
        Assert.DoesNotContain(api.Requests, request => request.Method == "PATCH" && !IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_WhenDeleteTargetDisappearsAfterApproval_RefusesMutation()
    {
        var exists = true;
        await using var api = new TestKubernetesApi(request => request.Method switch
        {
            "DELETE" when IsDryRun(request) => TestResponse.Json(StatusJson("Success", 200)),
            "GET" when exists => TestResponse.Json(ConfigMapJson("old")),
            "GET" => TestResponse.Json(StatusJson("NotFound", 404), statusCode: 404),
            _ => TestResponse.Json("{}")
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestDeleteManifestAsync(
            DemoNamespace,
            ConfigMapManifest,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);
        exists = false;

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("Live Kubernetes state changed after approval", result);
        Assert.DoesNotContain(api.Requests, request => request.Method == "DELETE" && !IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_WhenLiveStateMatchesStoredDiff_AppliesPlan()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo/scale" when request.Method == "PATCH" =>
                TestResponse.Json(ScaleJson(2)),
            "/apis/apps/v1/namespaces/demo/deployments/demo/scale" =>
                TestResponse.Json(ScaleJson(1)),
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json(DeploymentJson(2)),
            _ => StatusResponse(request, DeploymentJson(2))
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestScaleDeploymentAsync(
            DemoNamespace,
            "demo",
            2,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("Scaled apps/v1 Deployment demo/demo to 2 replicas.", result);
        Assert.Contains(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo/scale" &&
            !IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_AppliesManifestWithoutForce()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/api/v1/namespaces/demo/configmaps/demo-config" when request.Method == "PATCH" =>
                TestResponse.Json(ConfigMapJson("new")),
            "/api/v1/namespaces/demo/configmaps/demo-config" when request.Method == "GET" =>
                TestResponse.Json(ConfigMapJson("old")),
            _ => StatusResponse(request, DeploymentJson())
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestApplyManifestAsync(
            DemoNamespace,
            ConfigMapManifest,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);
        var patches = api.Requests.Where(request =>
            request.Method == "PATCH" &&
            request.Path == "/api/v1/namespaces/demo/configmaps/demo-config").ToArray();
        var dryRuns = patches.Where(IsDryRun).ToArray();
        var apply = Assert.Single(patches, request => !IsDryRun(request));

        Assert.Contains("Applied v1 ConfigMap demo/demo-config", result);
        Assert.Contains("No Deployments to wait for.", result);
        Assert.Equal(3, patches.Length);
        Assert.Equal(2, dryRuns.Length);
        Assert.Contains("fieldManager=infra-gate-mcp", apply.Query);
        Assert.DoesNotContain("force=", apply.Query, StringComparison.OrdinalIgnoreCase);
        Assert.All(dryRuns, request =>
        {
            Assert.Contains("dryRun=All", request.Query);
            Assert.Contains("fieldManager=infra-gate-mcp", request.Query);
            Assert.Contains("fieldValidation=Strict", request.Query);
            Assert.DoesNotContain("force=", request.Query, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_WhenPreApplyFieldOwnershipConflict_RefusesMutation()
    {
        int patchCount = 0;
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/api/v1/namespaces/demo/configmaps/demo-config" when request.Method == "PATCH" && ++patchCount == 2 =>
                TestResponse.Json(StatusJson("Conflict", 409), statusCode: 409),
            "/api/v1/namespaces/demo/configmaps/demo-config" when request.Method == "PATCH" =>
                TestResponse.Json(ConfigMapJson("new")),
            "/api/v1/namespaces/demo/configmaps/demo-config" when request.Method == "GET" =>
                TestResponse.Json(ConfigMapJson("old")),
            _ => StatusResponse(request, DeploymentJson())
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestApplyManifestAsync(
            DemoNamespace,
            ConfigMapManifest,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);
        var audit = await File.ReadAllTextAsync(context.ApprovalStore.AuditPath, CancellationToken.None);

        Assert.Contains("Apply refused by Kubernetes field ownership conflict.", result);
        Assert.Contains("force apply can take ownership of fields from another manager", result);
        Assert.DoesNotContain(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/api/v1/namespaces/demo/configmaps/demo-config" &&
            !IsDryRun(request));
        Assert.Contains(ApprovalConventions.AuditEvents.DryRunFailed, audit);
        Assert.Contains("field ownership conflict", audit);
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_WhenFinalFieldOwnershipConflict_AuditsFailure()
    {
        int patchCount = 0;
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/api/v1/namespaces/demo/configmaps/demo-config" when request.Method == "PATCH" && ++patchCount == 3 =>
                TestResponse.Json(StatusJson("Conflict", 409), statusCode: 409),
            "/api/v1/namespaces/demo/configmaps/demo-config" when request.Method == "PATCH" =>
                TestResponse.Json(ConfigMapJson("new")),
            "/api/v1/namespaces/demo/configmaps/demo-config" when request.Method == "GET" =>
                TestResponse.Json(ConfigMapJson("old")),
            _ => StatusResponse(request, DeploymentJson())
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestApplyManifestAsync(
            DemoNamespace,
            ConfigMapManifest,
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);
        var audit = await File.ReadAllTextAsync(context.ApprovalStore.AuditPath, CancellationToken.None);

        Assert.Contains("Apply refused by Kubernetes field ownership conflict.", result);
        Assert.Contains("force apply can take ownership of fields from another manager", result);
        Assert.Contains(ApprovalConventions.AuditEvents.ApplyFailed, audit);
        Assert.Contains("field ownership conflict", audit);
    }

    private static ManagerContext CreateManager(TestKubernetesApi? api = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new K8SMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { DemoNamespace },
            root);
        var approvalStore = new ApprovalStore(new ApprovalStoreOptions(root));
        var client = api is null
            ? null
            : new Kubernetes(new KubernetesClientConfiguration
            {
                Host = api.Url,
                SkipTlsVerify = true
            });

        return new ManagerContext(new K8sManager(options, approvalStore, client!, NullLogger<K8sManager>.Instance), approvalStore);
    }

    private static async Task<string> ApproveRequestPlanAsync(ManagerContext context, string requestText)
    {
        var planId = ParsePlanId(requestText);
        await ApprovePlanAsync(context, planId);

        return planId;
    }

    private static async Task ApprovePlanAsync(ManagerContext context, string planId)
    {
        var pending = await context.ApprovalStore.GetPendingPlanAsync(planId, CancellationToken.None);
        if (!pending.IsPending || pending.Envelope is null)
        {
            throw new InvalidOperationException(pending.Message);
        }

        await context.ApprovalStore.CreateGrantAsync(
            pending.Envelope,
            RequesterSubject,
            sourceChallengeId: "test-challenge",
            CancellationToken.None);
    }

    private static string ParsePlanId(string text) =>
        text.Split(Environment.NewLine)
            .Single(line => line.StartsWith("PlanId:", StringComparison.Ordinal))
            ["PlanId: ".Length..];

    private static bool IsDryRun(CapturedRequest request) =>
        request.Query.Contains("dryRun=All", StringComparison.Ordinal);

    private static K8sPlanDryRun CreateDryRun(params K8sObjectRef[] objects) =>
        new(
            K8sConventions.DryRunStatuses.Succeeded,
            DateTimeOffset.UtcNow,
            objects.Select(obj => new K8sPlanDryRunObject(
                $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}",
                "{}")).ToArray(),
            [],
            "Server-side dry-run succeeded.");

    private static K8sPlanDiff CreateDiff(K8sObjectRef obj, string? liveJson, string? proposedJson) =>
        K8sDiffService.BuildDiff(obj, liveJson, proposedJson);

    private static PlanEnvelope<KubernetesPlanPayload> CreatePlan(
        string planId,
        string operation,
        string namespaceName,
        string description,
        Dictionary<string, string> parameters,
        K8sObjectRef[] objects,
        string? manifest = null,
        K8sPlanDryRun? dryRun = null,
        K8sPlanDiff[]? diffs = null)
    {
        var payload = new KubernetesPlanPayload(namespaceName, description, parameters, objects)
        {
            Manifest = manifest,
            DryRun = dryRun,
            Diffs = diffs ?? []
        };

        return KubernetesApprovalAdapter.WithPayload(
            KubernetesApprovalAdapter.CreateEnvelope(
            planId,
            operation,
            DateTimeOffset.UtcNow,
            new PlanRequester(RequesterSubject, RequesterAuthenticationType),
            payload),
            payload);
    }

    private static TestResponse StatusResponse(CapturedRequest request, string deployment) => request.Path switch
    {
        "/apis/apps/v1/namespaces/demo/deployments" => TestResponse.Json(ListJson("apps/v1", "DeploymentList", [deployment])),
        "/api/v1/namespaces/demo/services" => TestResponse.Json(ListJson("v1", "ServiceList", [])),
        "/api/v1/namespaces/demo/configmaps" => TestResponse.Json(ListJson("v1", "ConfigMapList", [])),
        "/api/v1/namespaces/demo/pods" => TestResponse.Json(ListJson("v1", "PodList", [])),
        "/apis/apps/v1/namespaces/demo/replicasets" => TestResponse.Json(ListJson("apps/v1", "ReplicaSetList", [])),
        _ => TestResponse.Json("{}")
    };

    private static string DeploymentJson(int replicas = 1) =>
        $$"""
          {
            "apiVersion": "apps/v1",
            "kind": "Deployment",
            "metadata": {
              "name": "demo",
              "namespace": "demo",
              "generation": 1,
              "labels": { "app": "demo" }
            },
            "spec": {
              "replicas": {{replicas}},
              "selector": { "matchLabels": { "app": "demo" } },
              "template": {
                "metadata": { "labels": { "app": "demo" } },
                "spec": {
                  "containers": [{ "name": "nginx", "image": "nginx:1.27-alpine" }]
                }
              }
            },
            "status": {
              "observedGeneration": 1,
              "readyReplicas": {{replicas}},
              "availableReplicas": {{replicas}},
              "updatedReplicas": {{replicas}}
            }
          }
          """;

    private static string ScaleJson(int replicas) =>
        $$"""
          {
            "apiVersion": "autoscaling/v1",
            "kind": "Scale",
            "metadata": {
              "name": "demo",
              "namespace": "demo"
            },
            "spec": {
              "replicas": {{replicas}}
            },
            "status": {
              "replicas": {{replicas}}
            }
          }
          """;

    private static string ServiceJson(string name = "demo") =>
        $$"""
        {
          "apiVersion": "v1",
          "kind": "Service",
          "metadata": {
            "name": "{{name}}",
            "namespace": "demo"
          },
          "spec": {
            "selector": { "app": "demo" },
            "ports": [{ "port": 80, "targetPort": 80 }]
          }
        }
        """;

    private static string ConfigMapJson(string value) =>
        $$"""
          {
            "apiVersion": "v1",
            "kind": "ConfigMap",
            "metadata": {
              "name": "demo-config",
              "namespace": "demo"
            },
            "data": {
              "hello": "{{value}}"
            }
          }
          """;

    private static string StatusJson(string reason, int code) =>
        $$"""
          {
            "apiVersion": "v1",
            "kind": "Status",
            "status": "{{reason}}",
            "reason": "{{reason}}",
            "code": {{code}}
          }
          """;

    private static string ListJson(string apiVersion, string kind, IEnumerable<string> items) =>
        $$"""
          {
            "apiVersion": "{{apiVersion}}",
            "kind": "{{kind}}",
            "items": [
              {{string.Join(",", items)}}
            ]
          }
          """;

    private static string LegacyApprovedPath(ApprovalStore store, string planId) =>
        Path.Combine(
            Path.GetDirectoryName(store.PendingDirectory)!,
            "approved",
            planId + ApprovalConventions.Storage.Sha256Extension);

    private const string DeleteManifest = """
                                          apiVersion: v1
                                          kind: Service
                                          metadata:
                                            name: demo
                                          spec:
                                            selector:
                                              app: demo
                                            ports:
                                              - port: 80
                                                targetPort: 80
                                          ---
                                          apiVersion: v1
                                          kind: ConfigMap
                                          metadata:
                                            name: missing-config
                                          data:
                                            hello: world
                                          """;

    private const string MismatchedApplyManifest = """
                                                   apiVersion: v1
                                                   kind: ConfigMap
                                                   metadata:
                                                     name: different-config
                                                   data:
                                                     hello: world
                                                   """;

    private const string ConfigMapManifest = """
                                             apiVersion: v1
                                             kind: ConfigMap
                                             metadata:
                                               name: demo-config
                                             data:
                                               hello: new
                                             """;

    private const string PrivilegedDeploymentManifest = """
                                                        apiVersion: apps/v1
                                                        kind: Deployment
                                                        metadata:
                                                          name: demo
                                                        spec:
                                                          replicas: 1
                                                          selector:
                                                            matchLabels:
                                                              app: demo
                                                          template:
                                                            metadata:
                                                              labels:
                                                                app: demo
                                                            spec:
                                                              containers:
                                                                - name: nginx
                                                                  image: nginx:1.27-alpine
                                                                  securityContext:
                                                                    privileged: true
                                                        """;

    private sealed record ManagerContext(K8sManager Manager, ApprovalStore ApprovalStore);
}
