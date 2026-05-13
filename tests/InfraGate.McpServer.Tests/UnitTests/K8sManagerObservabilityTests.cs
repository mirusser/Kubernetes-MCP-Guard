using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.McpServer;
using k8s;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sManagerObservabilityTests
{
    [Fact]
    public async Task GetEventsAsync_RejectsDisallowedNamespace()
    {
        var manager = CreateManager();

        var result = await manager.GetEventsAsync("other", null, null, 50, CancellationToken.None);

        Assert.Contains("Namespace 'other' is not allowed", result);
    }

    [Fact]
    public async Task GetEventsAsync_RejectsLimitOutsideBounds()
    {
        var manager = CreateManager();

        var result = await manager.GetEventsAsync("demo", null, null, 101, CancellationToken.None);

        Assert.Contains("Limit must be between 1 and 100", result);
    }

    [Fact]
    public async Task GetEventsAsync_RejectsLimitBelowBounds()
    {
        var manager = CreateManager();

        var result = await manager.GetEventsAsync("demo", null, null, 0, CancellationToken.None);

        Assert.Contains("Limit must be between 1 and 100", result);
    }

    [Fact]
    public async Task GetPodLogsAsync_RejectsTailLinesOutsideBounds()
    {
        var manager = CreateManager();

        var result = await manager.GetPodLogsAsync("demo", "demo-pod", null, 501, previous: false, CancellationToken.None);

        Assert.Contains("TailLines must be between 1 and 500", result);
    }

    [Fact]
    public async Task GetPodLogsAsync_RejectsTailLinesBelowBounds()
    {
        var manager = CreateManager();

        var result = await manager.GetPodLogsAsync("demo", "demo-pod", null, 0, previous: false, CancellationToken.None);

        Assert.Contains("TailLines must be between 1 and 500", result);
    }

    [Fact]
    public async Task GetResourceAsync_RejectsSecretKind()
    {
        var manager = CreateManager();

        var result = await manager.GetResourceAsync("demo", "Secret", "demo-secret", CancellationToken.None);

        Assert.Contains("Secret resource details are intentionally unavailable", result);
    }

    [Fact]
    public async Task GetResourceAsync_RejectsUnsupportedKind()
    {
        var manager = CreateManager();

        var result = await manager.GetResourceAsync("demo", "Ingress", "demo-ingress", CancellationToken.None);

        Assert.Contains("Unsupported resource kind 'Ingress'", result);
    }

    [Fact]
    public async Task GetEventsAsync_ReturnsCompactEventSummary()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json("""
                                                                           {
                                                                             "apiVersion": "events.k8s.io/v1",
                                                                             "kind": "EventList",
                                                                             "metadata": { "resourceVersion": "1" },
                                                                             "items": [
                                                                               {
                                                                                 "metadata": { "name": "demo-event", "namespace": "demo" },
                                                                                 "type": "Warning",
                                                                                 "reason": "Failed",
                                                                                 "action": "Pulling",
                                                                                 "note": "failed to pull image",
                                                                                 "eventTime": "2026-04-29T00:00:00Z",
                                                                                 "reportingController": "kubelet",
                                                                                 "reportingInstance": "node-1",
                                                                                 "regarding": {
                                                                                   "apiVersion": "v1",
                                                                                   "kind": "Pod",
                                                                                   "namespace": "demo",
                                                                                   "name": "demo-pod"
                                                                                 }
                                                                               }
                                                                             ]
                                                                           }
                                                                           """));
        var manager = CreateManager(api);

        var result = await manager.GetEventsAsync(
            "demo",
            "app=demo",
            "regarding.name=demo-pod",
            2,
            CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        Assert.Equal("demo", root.GetProperty("namespace").GetString());
        Assert.Equal(2, root.GetProperty("limit").GetInt32());
        Assert.Equal("demo-event", root.GetProperty("events")[0].GetProperty("name").GetString());
        Assert.Equal("Pod", root.GetProperty("events")[0].GetProperty("regarding").GetProperty("kind").GetString());
        Assert.Equal("/apis/events.k8s.io/v1/namespaces/demo/events", api.LastRequest?.Path);
        Assert.Contains("labelSelector=app%3Ddemo", api.LastRequest?.Query);
        Assert.Contains("fieldSelector=regarding.name%3Ddemo-pod", api.LastRequest?.Query);
        Assert.Contains("limit=2", api.LastRequest?.Query);
    }

    [Fact]
    public async Task GetPodLogsAsync_ReturnsBoundedLogSummary()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Text("line one\nline two"));
        var manager = CreateManager(api);

        var result = await manager.GetPodLogsAsync(
            "demo",
            "demo-pod",
            "web",
            7,
            previous: true,
            CancellationToken.None);

        Assert.StartsWith("{", result);
        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        Assert.Equal("demo-pod", root.GetProperty("podName").GetString());
        Assert.Equal("web", root.GetProperty("container").GetString());
        Assert.Equal(7, root.GetProperty("tailLines").GetInt32());
        Assert.Equal(65536, root.GetProperty("limitBytes").GetInt32());
        Assert.Equal("line one\nline two", root.GetProperty("log").GetString());
        Assert.Equal("/api/v1/namespaces/demo/pods/demo-pod/log", api.LastRequest?.Path);
        Assert.Contains("container=web", api.LastRequest?.Query);
        Assert.Contains("limitBytes=65536", api.LastRequest?.Query);
        Assert.Contains("previous=true", api.LastRequest?.Query);
        Assert.Contains("tailLines=7", api.LastRequest?.Query);
    }

    [Fact]
    public async Task GetResourceAsync_ReturnsConfigMapSummaryWithoutValues()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json("""
                                                                           {
                                                                             "apiVersion": "v1",
                                                                             "kind": "ConfigMap",
                                                                             "metadata": {
                                                                               "name": "demo-config",
                                                                               "namespace": "demo",
                                                                               "labels": { "app": "demo" }
                                                                             },
                                                                             "immutable": true,
                                                                             "data": {
                                                                               "password": "supersecret",
                                                                               "setting": "enabled"
                                                                             },
                                                                             "binaryData": {
                                                                               "blob": "AAAA"
                                                                             }
                                                                           }
                                                                           """));
        var manager = CreateManager(api);

        var result = await manager.GetResourceAsync("demo", "ConfigMap", "demo-config", CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        Assert.Equal("ConfigMap", root.GetProperty("kind").GetString());
        Assert.Equal("demo-config", root.GetProperty("name").GetString());
        Assert.Contains(root.GetProperty("dataKeys").EnumerateArray(), key => key.GetString() == "password");
        Assert.Contains(root.GetProperty("binaryDataKeys").EnumerateArray(), key => key.GetString() == "blob");
        Assert.DoesNotContain("supersecret", result);
        Assert.DoesNotContain("enabled", result);
        Assert.DoesNotContain("AAAA", result);
        Assert.Equal("/api/v1/namespaces/demo/configmaps/demo-config", api.LastRequest?.Path);
    }

    [Fact]
    public async Task GetResourceAsync_ReturnsSupportedResourceSummaries()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json("""
                                                                                   {
                                                                                     "apiVersion": "apps/v1",
                                                                                     "kind": "Deployment",
                                                                                     "metadata": { "name": "demo", "namespace": "demo", "labels": { "app": "demo" } },
                                                                                     "spec": {
                                                                                       "replicas": 2,
                                                                                       "selector": { "matchLabels": { "app": "demo" } }
                                                                                     },
                                                                                     "status": { "readyReplicas": 1, "availableReplicas": 1, "updatedReplicas": 1 }
                                                                                   }
                                                                                   """),
            "/apis/apps/v1/namespaces/demo/replicasets/demo-rs" => TestResponse.Json("""
                                                                                     {
                                                                                       "apiVersion": "apps/v1",
                                                                                       "kind": "ReplicaSet",
                                                                                       "metadata": { "name": "demo-rs", "namespace": "demo", "labels": { "app": "demo" } },
                                                                                       "spec": {
                                                                                         "replicas": 2,
                                                                                         "selector": { "matchLabels": { "app": "demo" } }
                                                                                       },
                                                                                       "status": { "readyReplicas": 1, "availableReplicas": 1 }
                                                                                     }
                                                                                     """),
            "/api/v1/namespaces/demo/pods/demo-pod" => TestResponse.Json("""
                                                                         {
                                                                           "apiVersion": "v1",
                                                                           "kind": "Pod",
                                                                           "metadata": { "name": "demo-pod", "namespace": "demo", "labels": { "app": "demo" } },
                                                                           "status": {
                                                                             "phase": "Running",
                                                                             "containerStatuses": [{
                                                                               "name": "nginx",
                                                                               "ready": true,
                                                                               "restartCount": 0,
                                                                               "state": { "running": {} }
                                                                             }]
                                                                           },
                                                                           "spec": {
                                                                             "containers": [{
                                                                               "name": "nginx",
                                                                               "image": "nginx:1.27-alpine",
                                                                               "env": [{ "name": "PASSWORD", "value": "secret-env" }]
                                                                             }]
                                                                           }
                                                                         }
                                                                         """),
            "/api/v1/namespaces/demo/services/demo" => TestResponse.Json("""
                                                                          {
                                                                            "apiVersion": "v1",
                                                                            "kind": "Service",
                                                                            "metadata": { "name": "demo", "namespace": "demo", "labels": { "app": "demo" } },
                                                                            "spec": {
                                                                              "type": "ClusterIP",
                                                                              "clusterIP": "10.0.0.1",
                                                                              "selector": { "app": "demo" },
                                                                              "ports": [{ "name": "http", "port": 80, "targetPort": 80, "protocol": "TCP" }]
                                                                            }
                                                                          }
                                                                          """),
            _ => TestResponse.Json("{}")
        });
        var manager = CreateManager(api);

        var deployment = await manager.GetResourceAsync("demo", "Deployment", "demo", CancellationToken.None);
        var replicaSet = await manager.GetResourceAsync("demo", "ReplicaSet", "demo-rs", CancellationToken.None);
        var pod = await manager.GetResourceAsync("demo", "Pod", "demo-pod", CancellationToken.None);
        var service = await manager.GetResourceAsync("demo", "Service", "demo", CancellationToken.None);

        AssertResourceSummary(deployment, "Deployment", "demo");
        AssertResourceSummary(replicaSet, "ReplicaSet", "demo-rs");
        AssertResourceSummary(pod, "Pod", "demo-pod");
        AssertResourceSummary(service, "Service", "demo");
        Assert.DoesNotContain("secret-env", pod);
    }

    private static void AssertResourceSummary(string json, string kind, string name)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(kind, root.GetProperty("kind").GetString());
        Assert.Equal(name, root.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetEventsAsync_WhenApiReturnsServerError_ReturnsFormattedError()
    {
        await using var api = new TestKubernetesApi(_ =>
            TestResponse.Json(StatusJson("InternalError", 500), statusCode: 500));
        var manager = CreateManager(api);

        var result = await manager.GetEventsAsync("demo", null, null, 50, CancellationToken.None);

        Assert.Contains("Event read failed", result);
        Assert.Contains("500", result);
    }

    [Fact]
    public async Task GetPodLogsAsync_WhenApiReturnsNotFound_ReturnsFormattedError()
    {
        await using var api = new TestKubernetesApi(_ =>
            TestResponse.Text("not found", statusCode: 404));
        var manager = CreateManager(api);

        var result = await manager.GetPodLogsAsync("demo", "demo-pod", null, 200, previous: false, CancellationToken.None);

        Assert.Contains("Pod log read failed", result);
    }

    private static K8sManager CreateManager(TestKubernetesApi? api = null)
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

        return new K8sManager(options, new ApprovalStore(new ApprovalStoreOptions(root)), client!, NullLogger<K8sManager>.Instance);
    }

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

}
