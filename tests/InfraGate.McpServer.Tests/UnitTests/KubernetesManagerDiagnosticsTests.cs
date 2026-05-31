using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using InfraGate.McpServer;
using k8s;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesManagerDiagnosticsTests
{
    [Fact]
    public async Task GetPodDiagnosticsAsync_RejectsDisallowedNamespace()
    {
        var manager = CreateManager().Manager;

        var result = await manager.GetPodDiagnosticsAsync("other", "demo-pod", 50, CancellationToken.None);

        Assert.Contains("Namespace 'other' is not allowed", result);
    }

    [Fact]
    public async Task GetDeploymentDiagnosticsAsync_RejectsLimitOutsideBounds()
    {
        var manager = CreateManager().Manager;

        var result = await manager.GetDeploymentDiagnosticsAsync("demo", "demo", 101, CancellationToken.None);

        Assert.Contains("Limit must be between 1 and 100", result);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_RejectsLimitBelowBounds()
    {
        var manager = CreateManager().Manager;

        var deploymentResult = await manager.GetDeploymentDiagnosticsAsync("demo", "demo", 0, CancellationToken.None);
        var podResult = await manager.GetPodDiagnosticsAsync("demo", "demo-pod", 0, CancellationToken.None);
        var serviceResult = await manager.GetServiceDiagnosticsAsync("demo", "demo", 0, CancellationToken.None);

        Assert.Contains("Limit must be between 1 and 100", deploymentResult);
        Assert.Contains("Limit must be between 1 and 100", podResult);
        Assert.Contains("Limit must be between 1 and 100", serviceResult);
    }

    [Fact]
    public async Task GetDeploymentDiagnosticsAsync_ReturnsBoundedRelatedSummaries()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json(DeploymentJson("nginx:1.27-alpine")),
            "/apis/apps/v1/namespaces/demo/replicasets" => TestResponse.Json(ListJson("ReplicaSetList", ReplicaSetItems(55))),
            "/api/v1/namespaces/demo/pods" => TestResponse.Json(ListJson("PodList", PodItems(55))),
            "/apis/events.k8s.io/v1/namespaces/demo/events" => TestResponse.Json("""
                                                                                    {
                                                                                      "apiVersion": "events.k8s.io/v1",
                                                                                      "kind": "EventList",
                                                                                      "items": [
                                                                                        {
                                                                                          "metadata": { "name": "other-warning" },
                                                                                          "type": "Warning",
                                                                                          "reason": "Other",
                                                                                          "note": "unrelated",
                                                                                          "regarding": { "kind": "Pod", "name": "other-pod", "namespace": "demo" }
                                                                                        },
                                                                                        {
                                                                                          "metadata": { "name": "pod-warning" },
                                                                                          "type": "Warning",
                                                                                          "reason": "BackOff",
                                                                                          "note": "container back-off",
                                                                                          "regarding": { "kind": "Pod", "name": "demo-pod-1", "namespace": "demo" }
                                                                                        }
                                                                                      ]
                                                                                    }
                                                                                    """),
            _ => TestResponse.Json("{}")
        });
        var manager = CreateManager(api).Manager;

        var result = await manager.GetDeploymentDiagnosticsAsync("demo", "demo", 10, CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        Assert.Equal("Deployment", root.GetProperty("kind").GetString());
        Assert.Equal("app=demo", root.GetProperty("selector").GetString());
        Assert.Equal(50, root.GetProperty("replicaSets").GetArrayLength());
        Assert.Equal(50, root.GetProperty("pods").GetArrayLength());
        Assert.Single(root.GetProperty("events").EnumerateArray());
        Assert.Equal("pod-warning", root.GetProperty("events")[0].GetProperty("name").GetString());
        Assert.DoesNotContain("supersecret", result);
        Assert.Contains(api.Requests, request =>
            request.Path == "/apis/apps/v1/namespaces/demo/replicasets" &&
            request.Query.Contains("labelSelector=app%3Ddemo", StringComparison.Ordinal));
        Assert.Contains(api.Requests, request =>
            request.Path == "/api/v1/namespaces/demo/pods" &&
            request.Query.Contains("labelSelector=app%3Ddemo", StringComparison.Ordinal));
        Assert.Contains(api.Requests, request =>
            request.Path == "/apis/events.k8s.io/v1/namespaces/demo/events" &&
            request.Query.Contains("limit=100", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetDeploymentDiagnosticsAsync_ForwardsMatchExpressionSelector()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json(DeploymentWithMatchExpressionsJson()),
            "/apis/apps/v1/namespaces/demo/replicasets" => TestResponse.Json(ListJson("ReplicaSetList", [])),
            "/api/v1/namespaces/demo/pods" => TestResponse.Json(ListJson("PodList", [])),
            "/apis/events.k8s.io/v1/namespaces/demo/events" => TestResponse.Json("""
                                                                                    {
                                                                                      "apiVersion": "events.k8s.io/v1",
                                                                                      "kind": "EventList",
                                                                                      "items": []
                                                                                    }
                                                                                    """),
            _ => TestResponse.Json("{}")
        });
        var manager = CreateManager(api).Manager;

        var result = await manager.GetDeploymentDiagnosticsAsync("demo", "demo", 10, CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        Assert.Equal("app=demo,tier in (api,worker),!debug", root.GetProperty("selector").GetString());
        Assert.Contains(api.Requests, request =>
            request.Path == "/api/v1/namespaces/demo/pods" &&
            DecodeQuery(request.Query).Contains(
                "labelSelector=app=demo,tier in (api,worker),!debug",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPodDiagnosticsAsync_ReturnsPodAndRelatedEventsWithoutLogs()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/api/v1/namespaces/demo/pods/demo-pod" => TestResponse.Json(PodJson("demo-pod")),
            "/apis/events.k8s.io/v1/namespaces/demo/events" => TestResponse.Json("""
                                                                                    {
                                                                                      "apiVersion": "events.k8s.io/v1",
                                                                                      "kind": "EventList",
                                                                                      "items": [
                                                                                        {
                                                                                          "metadata": { "name": "pod-event" },
                                                                                          "type": "Normal",
                                                                                          "reason": "Started",
                                                                                          "note": "started",
                                                                                          "regarding": { "kind": "Pod", "name": "demo-pod", "namespace": "demo" }
                                                                                        }
                                                                                      ]
                                                                                    }
                                                                                    """),
            _ => TestResponse.Json("{}")
        });
        var manager = CreateManager(api).Manager;

        var result = await manager.GetPodDiagnosticsAsync("demo", "demo-pod", 3, CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        Assert.Equal("Pod", root.GetProperty("kind").GetString());
        Assert.Equal("demo-pod", root.GetProperty("pod").GetProperty("name").GetString());
        Assert.Single(root.GetProperty("events").EnumerateArray());
        Assert.DoesNotContain("\"log\"", result);
        Assert.Contains(api.Requests, request =>
            request.Path == "/apis/events.k8s.io/v1/namespaces/demo/events" &&
            request.Query.Contains("limit=100", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetServiceDiagnosticsAsync_UsesServiceSelectorWithoutReadingEndpoints()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/api/v1/namespaces/demo/services/demo" => TestResponse.Json("""
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
                                                                          """),
            "/api/v1/namespaces/demo/pods" => TestResponse.Json(ListJson("PodList", PodItems(1))),
            "/apis/events.k8s.io/v1/namespaces/demo/events" => TestResponse.Json("""
                                                                                    {
                                                                                      "apiVersion": "events.k8s.io/v1",
                                                                                      "kind": "EventList",
                                                                                      "items": [
                                                                                        {
                                                                                          "metadata": { "name": "service-event" },
                                                                                          "type": "Normal",
                                                                                          "reason": "ServiceUpdated",
                                                                                          "note": "service updated",
                                                                                          "regarding": { "kind": "Service", "name": "demo", "namespace": "demo" }
                                                                                        },
                                                                                        {
                                                                                          "metadata": { "name": "pod-event" },
                                                                                          "type": "Normal",
                                                                                          "reason": "Started",
                                                                                          "note": "pod started",
                                                                                          "regarding": { "kind": "Pod", "name": "demo-pod-1", "namespace": "demo" }
                                                                                        },
                                                                                        {
                                                                                          "metadata": { "name": "unrelated-event" },
                                                                                          "type": "Warning",
                                                                                          "reason": "Unrelated",
                                                                                          "note": "ignore me",
                                                                                          "regarding": { "kind": "Deployment", "name": "other", "namespace": "demo" }
                                                                                        }
                                                                                      ]
                                                                                    }
                                                                                    """),
            _ => TestResponse.Json("{}")
        });
        var manager = CreateManager(api).Manager;

        var result = await manager.GetServiceDiagnosticsAsync("demo", "demo", 5, CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        Assert.Equal("Service", root.GetProperty("kind").GetString());
        Assert.Equal("app=demo", root.GetProperty("selector").GetString());
        Assert.Single(root.GetProperty("pods").EnumerateArray());
        Assert.Equal(2, root.GetProperty("events").GetArrayLength());
        Assert.Equal("service-event", root.GetProperty("events")[0].GetProperty("name").GetString());
        Assert.Equal("pod-event", root.GetProperty("events")[1].GetProperty("name").GetString());
        Assert.DoesNotContain(api.Requests, request =>
            request.Path.Contains("endpoints", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(api.Requests, request =>
            request.Path == "/apis/events.k8s.io/v1/namespaces/demo/events" &&
            request.Query.Contains("limit=100", StringComparison.Ordinal));
    }

    private static ManagerContext CreateManager(TestKubernetesApi? api = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new KubernetesMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { "demo" });
        var client = api is null
            ? null
            : new Kubernetes(new KubernetesClientConfiguration
            {
                Host = api.Url,
                SkipTlsVerify = true
            });

        return new ManagerContext(new KubernetesManager(options, client!, NullLogger<KubernetesManager>.Instance), root);
    }

    private static string DeploymentJson(string image) =>
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
              "replicas": 1,
              "selector": { "matchLabels": { "app": "demo" } },
              "template": {
                "metadata": { "labels": { "app": "demo" } },
                "spec": {
                  "containers": [{ "name": "nginx", "image": "{{image}}" }]
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

    private static string DeploymentWithMatchExpressionsJson() =>
        """
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
            "selector": {
              "matchLabels": { "app": "demo" },
              "matchExpressions": [
                {
                  "key": "tier",
                  "operator": "In",
                  "values": ["worker", "api"]
                },
                {
                  "key": "debug",
                  "operator": "DoesNotExist"
                }
              ]
            },
            "template": {
              "metadata": { "labels": { "app": "demo", "tier": "api" } },
              "spec": {
                "containers": [{ "name": "nginx", "image": "nginx:1.27-alpine" }]
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

    private static string PodJson(string name) =>
        $$"""
          {
            "apiVersion": "v1",
            "kind": "Pod",
            "metadata": {
              "name": "{{name}}",
              "namespace": "demo",
              "labels": { "app": "demo" }
            },
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

    private static string ListJson(string kind, IEnumerable<string> items) =>
        $$"""
          {
            "apiVersion": "v1",
            "kind": "{{kind}}",
            "items": [
              {{string.Join(",", items)}}
            ]
          }
          """;

    private static IEnumerable<string> ReplicaSetItems(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index =>
                $$"""
                  {
                    "apiVersion": "apps/v1",
                    "kind": "ReplicaSet",
                    "metadata": { "name": "demo-rs-{{index}}", "namespace": "demo", "labels": { "app": "demo" } },
                    "spec": { "replicas": 1 },
                    "status": { "readyReplicas": 1, "availableReplicas": 1 }
                  }
                  """);
    }

    private static IEnumerable<string> PodItems(int count)
    {
        return Enumerable.Range(1, count).Select(index => PodJson($"demo-pod-{index}"));
    }

    private static string DecodeQuery(string query)
    {
        return Uri.UnescapeDataString(query.Replace("+", " ", StringComparison.Ordinal));
    }

    private sealed record class ManagerContext(KubernetesManager Manager, string ApprovalRoot);

}
