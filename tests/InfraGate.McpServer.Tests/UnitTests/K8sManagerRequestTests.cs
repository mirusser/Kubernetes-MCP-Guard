using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.McpServer;
using k8s;
using k8s.Models;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sManagerRequestTests
{
    [Fact]
    public async Task RequestApplyManifestAsync_CreatesPlan_ForSupportedManifest()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json("{}"));
        var context = CreateContext("demo", api);

        var result = await context.Manager.RequestApplyManifestAsync("demo", ValidManifest, CancellationToken.None);
        var planId = ParsePlanId(result);
        var pending = await File.ReadAllTextAsync(
            context.ApprovalStore.GetPendingPath(planId),
            CancellationToken.None);

        Assert.Contains("Operation: apply", result);
        Assert.Contains("apps/v1 Deployment demo/demo", result);
        Assert.Contains("v1 Service demo/demo", result);
        Assert.Contains("v1 ConfigMap demo/demo-config", result);
        Assert.Contains("Dry-run: succeeded", result);
        Assert.Contains("\"dryRun\":", pending);
        Assert.Equal(3, api.Requests.Count(request => request.Method == "PATCH"));
        Assert.All(api.Requests.Where(request => request.Method == "PATCH"), request =>
        {
            Assert.Contains("dryRun=All", request.Query);
            Assert.Contains("fieldManager=infra-gate-mcp", request.Query);
            Assert.Contains("fieldValidation=Strict", request.Query);
        });
    }

    [Fact]
    public async Task RequestApplyManifestAsync_RejectsDisallowedNamespace()
    {
        var manager = CreateManager("demo");

        var result = await manager.RequestApplyManifestAsync("other", ValidManifest, CancellationToken.None);

        Assert.Contains("Namespace 'other' is not allowed", result);
    }

    [Fact]
    public async Task RequestScaleDeploymentAsync_RejectsReplicaCountOutsideBounds()
    {
        var manager = CreateManager("demo");

        var result = await manager.RequestScaleDeploymentAsync("demo", "demo", 6, CancellationToken.None);

        Assert.Contains("Replicas must be between 0 and 5", result);
    }

    [Fact]
    public async Task RequestScaleDeploymentAsync_DirectsApprovalThroughMcpServer()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json("{}"));
        var context = CreateContext("demo", api);

        var result = await context.Manager.RequestScaleDeploymentAsync("demo", "demo", 4, CancellationToken.None);

        Assert.Contains("Status: pending Gateway approval", result);
        Assert.Contains("Dry-run: succeeded", result);
        Assert.Contains("The Gateway will return a browser approval URL before applying it", result);
        Assert.DoesNotContain("./scripts/approve-plan.sh", result);
        var dryRun = Assert.Single(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo/scale");
        Assert.Contains("dryRun=All", dryRun.Query);
        Assert.Contains("fieldValidation=Strict", dryRun.Query);
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_RefusesPendingPlanWithoutApproval()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json("{}"));
        var context = CreateContext("demo", api);
        var request = await context.Manager.RequestScaleDeploymentAsync("demo", "demo", 4, CancellationToken.None);
        var planId = ParsePlanId(request);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("Refused:", result);
        Assert.Contains("not approved", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestApplyManifestAsync_WhenDryRunFails_DoesNotCreatePlan()
    {
        await using var api = new TestKubernetesApi(_ =>
            TestResponse.Json(StatusJson("Invalid", 422), statusCode: 422));
        var context = CreateContext("demo", api);

        var result = await context.Manager.RequestApplyManifestAsync("demo", ValidManifest, CancellationToken.None);

        Assert.Contains("Server-side dry-run failed", result);
        Assert.DoesNotContain("PlanId:", result);
        Assert.Empty(Directory.EnumerateFiles(context.ApprovalStore.PendingDirectory));
    }

    [Fact]
    public async Task RequestDeleteManifestAsync_WhenDryRunDeleteFails_DoesNotCreatePlan()
    {
        await using var api = new TestKubernetesApi(_ =>
            TestResponse.Json(StatusJson("NotFound", 404), statusCode: 404));
        var context = CreateContext("demo", api);

        var result = await context.Manager.RequestDeleteManifestAsync("demo", DeleteManifest, CancellationToken.None);

        Assert.Contains("Server-side dry-run failed", result);
        Assert.DoesNotContain("PlanId:", result);
        Assert.Empty(Directory.EnumerateFiles(context.ApprovalStore.PendingDirectory));
    }

    [Fact]
    public async Task RequestDeleteManifestAsync_SendsDryRunDeleteOptionsBody()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(StatusJson("Success", 200)));
        var context = CreateContext("demo", api);

        var result = await context.Manager.RequestDeleteManifestAsync("demo", DeleteManifest, CancellationToken.None);

        var request = Assert.Single(api.Requests, request =>
            request.Method == "DELETE" &&
            request.Path == "/api/v1/namespaces/demo/configmaps/demo-config");
        var options = JsonSerializer.Deserialize<V1DeleteOptions>(request.Body);
        var dryRun = Assert.Single(options?.DryRun ?? []);
        Assert.Contains("Dry-run: succeeded", result);
        Assert.Equal(K8sConventions.K8sApi.DryRunAll, dryRun);
    }

    private static K8sManager CreateManager(string namespaceName)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new K8sMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { namespaceName },
            root);

        return new K8sManager(options, new ApprovalStore(new ApprovalStoreOptions(root)), client: null!);
    }

    private static ManagerContext CreateContext(string namespaceName, TestKubernetesApi api)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new K8sMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { namespaceName },
            root);
        var approvalStore = new ApprovalStore(new ApprovalStoreOptions(root));
        var client = new Kubernetes(new KubernetesClientConfiguration
        {
            Host = api.Url,
            SkipTlsVerify = true
        });

        return new ManagerContext(new K8sManager(options, approvalStore, client), approvalStore);
    }

    private static string ParsePlanId(string text) =>
        text.Split(Environment.NewLine)
            .Single(line => line.StartsWith("PlanId:", StringComparison.Ordinal))
            ["PlanId: ".Length..];

    private static string StatusJson(string reason, int code) =>
        $$"""
          {
            "apiVersion": "v1",
            "kind": "Status",
            "status": "{{reason}}",
            "reason": "{{reason}}",
            "message": "{{reason}}",
            "code": {{code}}
          }
          """;

    private const string ValidManifest = """
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
                                         ---
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
                                           name: demo-config
                                         data:
                                           hello: world
                                         """;

    private const string DeleteManifest = """
                                          apiVersion: v1
                                          kind: ConfigMap
                                          metadata:
                                            name: demo-config
                                          data:
                                            hello: world
                                          """;

    private sealed record ManagerContext(K8sManager Manager, ApprovalStore ApprovalStore);
}
