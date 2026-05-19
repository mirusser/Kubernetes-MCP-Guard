using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using InfraGate.McpServer;
using k8s;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sManagerStatusTests
{
    [Fact]
    public async Task GetStatusAsync_RejectsDisallowedNamespace()
    {
        var manager = CreateManager();

        var result = await manager.GetStatusAsync("other", null, CancellationToken.None);

        Assert.Contains("Namespace 'other' is not allowed", result);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsShapeWithAllFiveCollections()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(EmptyList("List")));
        var manager = CreateManager(api);

        var result = await manager.GetStatusAsync("demo", null, CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("deployments", out _));
        Assert.True(root.TryGetProperty("services", out _));
        Assert.True(root.TryGetProperty("configMaps", out _));
        Assert.True(root.TryGetProperty("pods", out _));
        Assert.True(root.TryGetProperty("replicaSets", out _));
        Assert.Equal("demo", root.GetProperty("namespace").GetString());
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsEmptyCollectionsForEmptyCluster()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(EmptyList("List")));
        var manager = CreateManager(api);

        var result = await manager.GetStatusAsync("demo", null, CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        Assert.Empty(root.GetProperty("deployments").EnumerateArray());
        Assert.Empty(root.GetProperty("services").EnumerateArray());
        Assert.Empty(root.GetProperty("configMaps").EnumerateArray());
        Assert.Empty(root.GetProperty("pods").EnumerateArray());
        Assert.Empty(root.GetProperty("replicaSets").EnumerateArray());
    }

    [Fact]
    public async Task GetStatusAsync_HitsCorrectApiPaths()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(EmptyList("List")));
        var manager = CreateManager(api);

        await manager.GetStatusAsync("demo", null, CancellationToken.None);

        Assert.Contains(api.Requests, r => r.Path == "/apis/apps/v1/namespaces/demo/deployments");
        Assert.Contains(api.Requests, r => r.Path == "/api/v1/namespaces/demo/services");
        Assert.Contains(api.Requests, r => r.Path == "/api/v1/namespaces/demo/configmaps");
        Assert.Contains(api.Requests, r => r.Path == "/api/v1/namespaces/demo/pods");
        Assert.Contains(api.Requests, r => r.Path == "/apis/apps/v1/namespaces/demo/replicasets");
    }

    [Fact]
    public async Task GetStatusAsync_ForwardsLabelSelector()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(EmptyList("List")));
        var manager = CreateManager(api);

        var result = await manager.GetStatusAsync("demo", "app=demo", CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        Assert.Equal("app=demo", document.RootElement.GetProperty("labelSelector").GetString());
        Assert.True(api.Requests.All(r => r.Query.Contains("labelSelector", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetStatusAsync_SummarisesDeploymentReplicaCountsCorrectly()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments" => TestResponse.Json($$"""
                {
                  "apiVersion": "apps/v1",
                  "kind": "DeploymentList",
                  "items": [{
                    "metadata": { "name": "demo-app", "namespace": "demo", "labels": { "app": "demo" } },
                    "spec": { "replicas": 3 },
                    "status": {
                      "readyReplicas": 2,
                      "availableReplicas": 2,
                      "updatedReplicas": 3
                    }
                  }]
                }
                """),
            _ => TestResponse.Json(EmptyList("List"))
        });
        var manager = CreateManager(api);

        var result = await manager.GetStatusAsync("demo", null, CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var deployment = document.RootElement.GetProperty("deployments")[0];
        Assert.Equal("demo-app", deployment.GetProperty("name").GetString());
        var replicas = deployment.GetProperty("replicas");
        Assert.Equal(3, replicas.GetProperty("desired").GetInt32());
        Assert.Equal(2, replicas.GetProperty("ready").GetInt32());
        Assert.Equal(2, replicas.GetProperty("available").GetInt32());
        Assert.Equal(3, replicas.GetProperty("updated").GetInt32());
    }

    [Fact]
    public async Task GetStatusAsync_WhenKubernetesApiReturns500_ReturnsFormattedError()
    {
        await using var api = new TestKubernetesApi(_ =>
            TestResponse.Json(StatusJson("InternalError", 500), statusCode: 500));
        var manager = CreateManager(api);

        var result = await manager.GetStatusAsync("demo", null, CancellationToken.None);

        Assert.Contains("Status read failed", result);
        Assert.Contains("500", result);
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

        return new K8sManager(options, client!, NullLogger<K8sManager>.Instance);
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

    private static string EmptyList(string kind) =>
        $$"""
          {
            "apiVersion": "v1",
            "kind": "{{kind}}",
            "items": []
          }
          """;
}
