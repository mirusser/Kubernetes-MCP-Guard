using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.McpServer;
using k8s;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sManagerSetImageTests
{
    private const string RequesterSubject = "test-requester";
    private const string RequesterAuthenticationType = "test";

    [Fact]
    public async Task RequestSetDeploymentImageAsync_RejectsInvalidInputs()
    {
        var context = CreateManager();

        var disallowedNamespace = await context.Manager.RequestSetDeploymentImageAsync(
            "other",
            "demo",
            "nginx",
            "nginx:1.28-alpine",
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var blankName = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "",
            "nginx",
            "nginx:1.28-alpine",
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var blankContainer = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "",
            "nginx:1.28-alpine",
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var blankImage = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "nginx",
            "",
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);

        Assert.Contains("Namespace 'other' is not allowed", disallowedNamespace);
        Assert.Contains("Resource name is required", blankName);
        Assert.Contains("Container name is required", blankContainer);
        Assert.Contains("Image is required", blankImage);
    }

    [Fact]
    public async Task RequestSetDeploymentImageAsync_RejectsMissingContainer()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(DeploymentJson("nginx:1.27-alpine", includeSidecar: false)));
        var context = CreateManager(api);

        var result = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "sidecar",
            "nginx:1.28-alpine",
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);

        Assert.Contains("does not contain container 'sidecar'", result);
    }

    [Fact]
    public async Task RequestSetDeploymentImageAsync_CreatesPlanWithCurrentAndTargetImage()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(DeploymentJson("nginx:1.27-alpine")));
        var context = CreateManager(api);

        var result = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "nginx",
            "nginx:1.28-alpine",
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = ParsePlanId(result);
        var pending = await File.ReadAllTextAsync(
            context.ApprovalStore.GetPendingPath(planId),
            CancellationToken.None);

        Assert.Contains("Operation: set-image", result);
        Assert.Contains("apps/v1 Deployment demo/demo", result);
        Assert.Contains("Policy: not_applicable", result);
        AssertCompactSuccessfulResponse(result);
        Assert.Contains("\"currentImage\": \"nginx:1.27-alpine\"", pending);
        Assert.Contains("\"image\": \"nginx:1.28-alpine\"", pending);
        Assert.Contains("\"dryRun\":", pending);
        Assert.Contains(api.Requests, request => request.Method == "PATCH" && IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_RefusesStaleDeploymentImage()
    {
        var requestReadCompleted = false;
        await using var api = new TestKubernetesApi(request =>
        {
            if (request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo")
            {
                var image = requestReadCompleted ? "nginx:1.27.1-alpine" : "nginx:1.27-alpine";
                requestReadCompleted = true;

                return TestResponse.Json(DeploymentJson(image));
            }

            return TestResponse.Json("{}");
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "nginx",
            "nginx:1.28-alpine",
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApprovePlanAsync(context.ApprovalStore, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("image changed from planned 'nginx:1.27-alpine' to 'nginx:1.27.1-alpine'", result);
        Assert.DoesNotContain(api.Requests, request => request.Method == "PATCH" && !IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_RefusesMissingContainerAtApplyTime()
    {
        var requestReadCompleted = false;
        await using var api = new TestKubernetesApi(request =>
        {
            if (request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo")
            {
                var deployment = requestReadCompleted
                    ? DeploymentJson("nginx:1.27-alpine", includeNginx: false)
                    : DeploymentJson("nginx:1.27-alpine");
                requestReadCompleted = true;

                return TestResponse.Json(deployment);
            }

            return TestResponse.Json("{}");
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "nginx",
            "nginx:1.28-alpine",
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApprovePlanAsync(context.ApprovalStore, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("does not contain container 'nginx'", result);
        Assert.DoesNotContain(api.Requests, request => request.Method == "PATCH" && !IsDryRun(request));
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_PatchesOnlyPlannedContainerImage()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json(DeploymentJson("nginx:1.27-alpine")),
            "/apis/apps/v1/namespaces/demo/deployments" => TestResponse.Json(ListJson("apps/v1", "DeploymentList", [DeploymentJson("nginx:1.28-alpine")])),
            "/api/v1/namespaces/demo/services" => TestResponse.Json(ListJson("v1", "ServiceList", [])),
            "/api/v1/namespaces/demo/configmaps" => TestResponse.Json(ListJson("v1", "ConfigMapList", [])),
            "/api/v1/namespaces/demo/pods" => TestResponse.Json(ListJson("v1", "PodList", [])),
            "/apis/apps/v1/namespaces/demo/replicasets" => TestResponse.Json(ListJson("apps/v1", "ReplicaSetList", [])),
            _ => TestResponse.Json(DeploymentJson("nginx:1.28-alpine"))
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "nginx",
            "nginx:1.28-alpine",
            RequesterSubject,
            RequesterAuthenticationType,
            CancellationToken.None);
        var planId = await ApprovePlanAsync(context.ApprovalStore, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);
        var patch = Assert.Single(api.Requests, request => request.Method == "PATCH" && !IsDryRun(request));
        var dryRuns = api.Requests.Where(request => request.Method == "PATCH" && IsDryRun(request)).ToArray();

        Assert.Contains("Updated apps/v1 Deployment demo/demo container 'nginx' image", result);
        Assert.Equal("/apis/apps/v1/namespaces/demo/deployments/demo", patch.Path);
        Assert.Contains("fieldManager=infra-gate-mcp", patch.Query);
        Assert.Equal(2, dryRuns.Length);
        Assert.All(dryRuns, request => Assert.Contains("fieldValidation=Strict", request.Query));

        using var document = JsonDocument.Parse(patch.Body);
        AssertSingleProperty(document.RootElement, "spec");

        var spec = document.RootElement.GetProperty("spec");
        AssertSingleProperty(spec, "template");

        var template = spec.GetProperty("template");
        AssertSingleProperty(template, "spec");

        var templateSpec = template.GetProperty("spec");
        AssertSingleProperty(templateSpec, "containers");

        var patchedContainer = Assert.Single(templateSpec.GetProperty("containers").EnumerateArray());

        Assert.Equal(["name", "image"], patchedContainer.EnumerateObject().Select(property => property.Name));
        Assert.Equal("nginx", patchedContainer.GetProperty("name").GetString());
        Assert.Equal("nginx:1.28-alpine", patchedContainer.GetProperty("image").GetString());
        Assert.DoesNotContain("sidecar", patch.Body);
    }

    private static void AssertSingleProperty(JsonElement element, string propertyName)
    {
        var property = Assert.Single(element.EnumerateObject());

        Assert.Equal(propertyName, property.Name);
    }

    private static ManagerContext CreateManager(TestKubernetesApi? api = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new K8SMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { "demo" },
            root);
        var client = api is null
            ? null
            : new Kubernetes(new KubernetesClientConfiguration
            {
                Host = api.Url,
                SkipTlsVerify = true
            });

        var approvalStore = new ApprovalStore(new ApprovalStoreOptions(root));

        return new ManagerContext(new K8sManager(options, approvalStore, client!, NullLogger<K8sManager>.Instance), approvalStore);
    }

    private static async Task<string> ApprovePlanAsync(ApprovalStore store, string requestText)
    {
        var planId = ParsePlanId(requestText);
        var pending = await store.GetPendingPlanAsync(planId, CancellationToken.None);
        if (!pending.IsPending || pending.Envelope is null)
        {
            throw new InvalidOperationException(pending.Message);
        }

        await store.CreateGrantAsync(
            pending.Envelope,
            RequesterSubject,
            sourceChallengeId: "test-challenge",
            CancellationToken.None);

        return planId;
    }

    private static string ParsePlanId(string text) =>
        text.Split(Environment.NewLine)
            .Single(line => line.StartsWith("PlanId:", StringComparison.Ordinal))
            ["PlanId: ".Length..];

    private static void AssertCompactSuccessfulResponse(string result)
    {
        Assert.Contains("Status: pending_gateway_approval", result);
        Assert.Contains("Risk: medium", result);
        Assert.Contains("Next step: call apply_approved_plan with this PlanId.", result);
        Assert.DoesNotContain("Pending file:", result);
        Assert.DoesNotContain("Plan hash:", result);
        Assert.DoesNotContain("Dry-run:", result);
        Assert.DoesNotContain("Diff:", result);
        Assert.DoesNotContain("Manifest:", result);
    }

    private static bool IsDryRun(CapturedRequest request) =>
        request.Query.Contains("dryRun=All", StringComparison.Ordinal);

    private static string DeploymentJson(string image, bool includeSidecar = true, bool includeNginx = true)
    {
        var containers = new List<string>();
        if (includeNginx)
        {
            containers.Add($$"""{ "name": "nginx", "image": "{{image}}" }""");
        }

        if (includeSidecar)
        {
            containers.Add("""{ "name": "sidecar", "image": "busybox:1.36" }""");
        }

        return $$"""
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
                     "replicas": 1,
                     "selector": { "matchLabels": { "app": "demo" } },
                     "template": {
                       "metadata": { "labels": { "app": "demo" } },
                       "spec": {
                         "containers": [
                           {{string.Join(",", containers)}}
                         ]
                       }
                     }
                   },
                   "status": {
                     "observedGeneration": 1,
                     "readyReplicas": 1,
                     "availableReplicas": 1,
                     "updatedReplicas": 1
                   }
                 }
                 """;
    }

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

    private sealed record ManagerContext(K8sManager Manager, ApprovalStore ApprovalStore);

}
