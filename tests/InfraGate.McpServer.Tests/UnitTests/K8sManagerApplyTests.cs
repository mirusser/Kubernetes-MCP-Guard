using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.McpServer;
using k8s;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sManagerApplyTests
{
    private const string DemoNamespace = "demo";
    private const string PlanOperationApply = "apply";
    private const string PlanParameterObjectCount = "objectCount";

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
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);
        var patch = Assert.Single(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo/scale");

        Assert.Contains("Scaled apps/v1 Deployment demo/demo to 3 replicas.", result);
        Assert.Contains("Deployment rollout completed for demo.", result);
        Assert.Contains("fieldManager=infra-gate-mcp", patch.Query);

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
            CancellationToken.None);
        var planId = await ApproveRequestPlanAsync(context, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);
        var patch = Assert.Single(api.Requests, request =>
            request.Method == "PATCH" &&
            request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo");

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
            "/api/v1/namespaces/demo/configmaps/missing-config" when request.Method == "DELETE" =>
                TestResponse.Json(StatusJson("NotFound", 404), statusCode: 404),
            _ => StatusResponse(request, DeploymentJson())
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestDeleteManifestAsync(
            DemoNamespace,
            DeleteManifest,
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
        var context = CreateManager();
        var plan = new K8sPlan(
            "apply-mismatch",
            PlanOperationApply,
            DemoNamespace,
            DateTimeOffset.UtcNow,
            "Apply mismatched test manifest.",
            new Dictionary<string, string>
            {
                [PlanParameterObjectCount] = "1"
            },
            [new K8sObjectRef("v1", "Service", DemoNamespace, "planned-service")],
            MismatchedApplyManifest);
        await context.ApprovalStore.CreatePlanAsync(plan, CancellationToken.None);
        await ApprovePlanAsync(context, plan.Id);

        var result = await context.Manager.ApplyApprovedPlanAsync(plan.Id, CancellationToken.None);

        Assert.Contains("Apply plan manifest no longer matches the planned object references.", result);
    }

    private static ManagerContext CreateManager(TestKubernetesApi? api = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new K8sMcpOptions(
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

        return new ManagerContext(new K8sManager(options, approvalStore, client!), approvalStore);
    }

    private static async Task<string> ApproveRequestPlanAsync(ManagerContext context, string requestText)
    {
        var planId = ParsePlanId(requestText);
        await ApprovePlanAsync(context, planId);

        return planId;
    }

    private static async Task ApprovePlanAsync(ManagerContext context, string planId)
    {
        var hash = await ApprovalStore.ComputeSha256Async(
            context.ApprovalStore.GetPendingPath(planId),
            CancellationToken.None);
        await File.WriteAllTextAsync(
            context.ApprovalStore.GetApprovedPath(planId),
            hash,
            CancellationToken.None);
    }

    private static string ParsePlanId(string text) =>
        text.Split(Environment.NewLine)
            .Single(line => line.StartsWith("PlanId:", StringComparison.Ordinal))
            ["PlanId: ".Length..];

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

    private static string ServiceJson() =>
        """
        {
          "apiVersion": "v1",
          "kind": "Service",
          "metadata": {
            "name": "demo",
            "namespace": "demo"
          },
          "spec": {
            "selector": { "app": "demo" },
            "ports": [{ "port": 80, "targetPort": 80 }]
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

    private sealed record ManagerContext(K8sManager Manager, ApprovalStore ApprovalStore);
}
