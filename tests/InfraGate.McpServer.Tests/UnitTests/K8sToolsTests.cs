using Microsoft.Extensions.Logging.Abstractions;
using InfraGate.McpServer;
using k8s;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sToolsTests
{
    private const string DemoNamespace = "demo";

    [Fact]
    public async Task GetAllowedNamespaces_Delegates()
    {
        var manager = CreateManager();

        var result = await K8sTools.GetAllowedNamespaces(manager);

        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public async Task GetK8sStatus_Delegates()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments" => TestResponse.Json(EmptyListJson("apps/v1", "DeploymentList")),
            "/api/v1/namespaces/demo/services" => TestResponse.Json(EmptyListJson("v1", "ServiceList")),
            "/api/v1/namespaces/demo/configmaps" => TestResponse.Json(EmptyListJson("v1", "ConfigMapList")),
            "/api/v1/namespaces/demo/pods" => TestResponse.Json(EmptyListJson("v1", "PodList")),
            "/apis/apps/v1/namespaces/demo/replicasets" => TestResponse.Json(EmptyListJson("apps/v1", "ReplicaSetList")),
            _ => TestResponse.Json("{}")
        });
        var manager = CreateManager(api);

        var result = await K8sTools.GetK8sStatus(manager, DemoNamespace);

        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public async Task GetK8sEvents_Delegates()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(EmptyEventsJson()));
        var manager = CreateManager(api);

        var result = await K8sTools.GetK8sEvents(manager, DemoNamespace);

        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public async Task GetPodLogs_Delegates()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Text("log line one\nlog line two"));
        var manager = CreateManager(api);

        var result = await K8sTools.GetPodLogs(manager, DemoNamespace, "demo-pod");

        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public async Task GetK8sResource_Delegates()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(DeploymentJson()));
        var manager = CreateManager(api);

        var result = await K8sTools.GetK8sResource(manager, DemoNamespace, "Deployment", "demo");

        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public async Task GetDeploymentDiagnostics_Delegates()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json(DeploymentJson()),
            "/apis/apps/v1/namespaces/demo/replicasets" => TestResponse.Json(EmptyListJson("apps/v1", "ReplicaSetList")),
            "/api/v1/namespaces/demo/pods" => TestResponse.Json(EmptyListJson("v1", "PodList")),
            "/apis/events.k8s.io/v1/namespaces/demo/events" => TestResponse.Json(EmptyEventsJson()),
            _ => TestResponse.Json("{}")
        });
        var manager = CreateManager(api);

        var result = await K8sTools.GetDeploymentDiagnostics(manager, DemoNamespace, "demo");

        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public async Task GetPodDiagnostics_Delegates()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/api/v1/namespaces/demo/pods/demo-pod" => TestResponse.Json(PodJson()),
            "/apis/events.k8s.io/v1/namespaces/demo/events" => TestResponse.Json(EmptyEventsJson()),
            _ => TestResponse.Json("{}")
        });
        var manager = CreateManager(api);

        var result = await K8sTools.GetPodDiagnostics(manager, DemoNamespace, "demo-pod");

        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public async Task GetServiceDiagnostics_Delegates()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/api/v1/namespaces/demo/services/demo" => TestResponse.Json(ServiceJson()),
            "/api/v1/namespaces/demo/pods" => TestResponse.Json(EmptyListJson("v1", "PodList")),
            "/apis/events.k8s.io/v1/namespaces/demo/events" => TestResponse.Json(EmptyEventsJson()),
            _ => TestResponse.Json("{}")
        });
        var manager = CreateManager(api);

        var result = await K8sTools.GetServiceDiagnostics(manager, DemoNamespace, "demo");

        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void CheckLiveDrift_DoesNotExposePlanId()
    {
        var method = typeof(K8sTools).GetMethod(nameof(K8sTools.CheckLiveDrift));

        Assert.NotNull(method);
        Assert.DoesNotContain(method.GetParameters(), parameter => parameter.Name == "planId");
    }

    private static K8sManager CreateManager(TestKubernetesApi? api = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new K8SMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { DemoNamespace },
            root);
        var client = api is null
            ? null
            : new Kubernetes(new KubernetesClientConfiguration { Host = api.Url, SkipTlsVerify = true });
        return new K8sManager(options, client!, NullLogger<K8sManager>.Instance);
    }

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

    private static string PodJson() =>
        """
        {
          "apiVersion": "v1",
          "kind": "Pod",
          "metadata": { "name": "demo-pod", "namespace": "demo", "labels": { "app": "demo" } },
          "spec": {
            "nodeName": "minikube",
            "containers": [{ "name": "nginx", "image": "nginx:1.27-alpine" }]
          },
          "status": {
            "phase": "Running",
            "podIP": "10.1.0.1",
            "hostIP": "192.168.49.2",
            "containerStatuses": [{
              "name": "nginx",
              "ready": true,
              "restartCount": 0,
              "image": "nginx:1.27-alpine",
              "state": { "running": {} }
            }]
          }
        }
        """;

    private static string ServiceJson() =>
        """
        {
          "apiVersion": "v1",
          "kind": "Service",
          "metadata": { "name": "demo", "namespace": "demo" },
          "spec": {
            "type": "ClusterIP",
            "clusterIP": "10.0.0.1",
            "selector": { "app": "demo" },
            "ports": [{ "name": "http", "port": 80, "targetPort": 80, "protocol": "TCP" }]
          }
        }
        """;

    private static string EmptyListJson(string apiVersion, string kind) =>
        $$"""
          {
            "apiVersion": "{{apiVersion}}",
            "kind": "{{kind}}",
            "items": []
          }
          """;

    private static string EmptyEventsJson() =>
        """
        {
          "apiVersion": "events.k8s.io/v1",
          "kind": "EventList",
          "items": []
        }
        """;
}
