using InfraGate.Approvals;
using InfraGate.McpServer;
using InfraGate.McpServer.Diff;
using k8s;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sDiffServiceTests
{
    private static readonly K8sObjectRef DeploymentRef =
        new("apps/v1", "Deployment", "demo", "demo");

    private static readonly K8sObjectRef ConfigMapRef =
        new("v1", "ConfigMap", "demo", "demo-config");

    [Fact]
    public void BuildDiff_CreateObject_RecordsCreateSummaryAndAddedPaths()
    {
        var diff = K8sDiffService.BuildDiff(DeploymentRef, liveJson: null, DeploymentJson("nginx:1.27-alpine"));

        Assert.Equal(ApprovalConventions.DiffChangeTypes.Create, diff.ChangeType);
        Assert.Contains("will be created", diff.Summary);
        Assert.Contains("/spec/template/spec/containers/0/image", diff.AddedPaths);
        Assert.Contains("+apiVersion: apps/v1", diff.UnifiedDiff);
    }

    [Fact]
    public void BuildDiff_UpdateObject_ExcludesNoisyMetadataAndStatus()
    {
        var diff = K8sDiffService.BuildDiff(
            DeploymentRef,
            DeploymentJson("nginx:1.27-alpine", resourceVersion: "1", readyReplicas: 1),
            DeploymentJson("nginx:1.28-alpine", resourceVersion: "2", readyReplicas: 0));

        Assert.Equal(ApprovalConventions.DiffChangeTypes.Update, diff.ChangeType);
        Assert.Contains("/spec/template/spec/containers/0/image", diff.ChangedPaths);
        Assert.DoesNotContain("/metadata/resourceVersion", diff.ChangedPaths);
        Assert.DoesNotContain("/status/readyReplicas", diff.ChangedPaths);
        Assert.DoesNotContain("managedFields", diff.UnifiedDiff);
        Assert.DoesNotContain("resourceVersion", diff.UnifiedDiff);
        Assert.DoesNotContain("readyReplicas", diff.UnifiedDiff);
        Assert.DoesNotContain("last-applied-configuration", diff.UnifiedDiff);
    }

    [Fact]
    public void BuildDiff_DeleteObject_RecordsDeleteSummaryAndRemovedPaths()
    {
        var diff = K8sDiffService.BuildDiff(DeploymentRef, DeploymentJson("nginx:1.27-alpine"), proposedJson: null);

        Assert.Equal(ApprovalConventions.DiffChangeTypes.Delete, diff.ChangeType);
        Assert.Contains("will be deleted", diff.Summary);
        Assert.Contains("/spec/template/spec/containers/0/image", diff.RemovedPaths);
        Assert.Contains("-apiVersion: apps/v1", diff.UnifiedDiff);
    }

    [Fact]
    public void BuildDiff_NoChanges_RecordsNoOp()
    {
        var diff = K8sDiffService.BuildDiff(
            DeploymentRef,
            DeploymentJson("nginx:1.27-alpine", resourceVersion: "1", readyReplicas: 1),
            DeploymentJson("nginx:1.27-alpine", resourceVersion: "2", readyReplicas: 0));

        Assert.Equal(ApprovalConventions.DiffChangeTypes.NoOp, diff.ChangeType);
        Assert.Empty(diff.AddedPaths);
        Assert.Empty(diff.RemovedPaths);
        Assert.Empty(diff.ChangedPaths);
        Assert.Equal("No diff.", diff.UnifiedDiff);
    }

    [Fact]
    public void BuildDiff_ConfigMapChange_DoesNotExposeRemovedNoisyFields()
    {
        var diff = K8sDiffService.BuildDiff(
            ConfigMapRef,
            ConfigMapJson("world", resourceVersion: "1"),
            ConfigMapJson("universe", resourceVersion: "2"));

        Assert.Equal(ApprovalConventions.DiffChangeTypes.Update, diff.ChangeType);
        Assert.Contains("/data/hello", diff.ChangedPaths);
        Assert.DoesNotContain("managedFields", diff.UnifiedDiff);
        Assert.DoesNotContain("resourceVersion", diff.UnifiedDiff);
    }

    [Fact]
    public void BuildDiff_ObjectPropertyAdded_RecordsAddedPath()
    {
        var diff = K8sDiffService.BuildDiff(
            ConfigMapRef,
            """{"data":{"hello":"world"}}""",
            """{"data":{"hello":"world","new-key":"new-value"}}""");

        Assert.Equal(ApprovalConventions.DiffChangeTypes.Update, diff.ChangeType);
        Assert.Contains("/data/new-key", diff.AddedPaths);
        Assert.Empty(diff.RemovedPaths);
    }

    [Fact]
    public void BuildDiff_ObjectPropertyRemoved_RecordsRemovedPath()
    {
        var diff = K8sDiffService.BuildDiff(
            ConfigMapRef,
            """{"data":{"hello":"world","old-key":"old-value"}}""",
            """{"data":{"hello":"world"}}""");

        Assert.Equal(ApprovalConventions.DiffChangeTypes.Update, diff.ChangeType);
        Assert.Contains("/data/old-key", diff.RemovedPaths);
        Assert.Empty(diff.AddedPaths);
    }

    [Fact]
    public void BuildDiff_ArrayItemAdded_RecordsAddedPath()
    {
        var diff = K8sDiffService.BuildDiff(
            DeploymentRef,
            """{"spec":{"containers":[{"name":"app","image":"nginx:1.27"}]}}""",
            """{"spec":{"containers":[{"name":"app","image":"nginx:1.27"},{"name":"sidecar","image":"busybox:1.36"}]}}""");

        Assert.Equal(ApprovalConventions.DiffChangeTypes.Update, diff.ChangeType);
        Assert.Contains("/spec/containers/1/image", diff.AddedPaths);
        Assert.Contains("/spec/containers/1/name", diff.AddedPaths);
    }

    [Fact]
    public void BuildDiff_ArrayItemRemoved_RecordsRemovedPath()
    {
        var diff = K8sDiffService.BuildDiff(
            DeploymentRef,
            """{"spec":{"containers":[{"name":"app","image":"nginx:1.27"},{"name":"sidecar","image":"busybox:1.36"}]}}""",
            """{"spec":{"containers":[{"name":"app","image":"nginx:1.27"}]}}""");

        Assert.Equal(ApprovalConventions.DiffChangeTypes.Update, diff.ChangeType);
        Assert.Contains("/spec/containers/1/image", diff.RemovedPaths);
        Assert.Contains("/spec/containers/1/name", diff.RemovedPaths);
    }

    [Fact]
    public async Task FindDriftAsync_MissingDiffs_ReturnsMessage()
    {
        var client = new Kubernetes(new KubernetesClientConfiguration
        {
            Host = "http://127.0.0.1:1",
            SkipTlsVerify = true
        });
        var plan = new K8sPlan(
            "plan-1",
            "apply",
            "demo",
            DateTimeOffset.UtcNow,
            "Apply ConfigMap.",
            [],
            [ConfigMapRef],
            Manifest: null,
            Diffs: []);

        var drift = await K8sDiffService.FindDriftAsync(client, plan, CancellationToken.None);

        Assert.Equal("Plan 'plan-1' is missing recorded diff data. Re-request the plan.", drift);
    }

    private static string DeploymentJson(
        string image,
        string resourceVersion = "1",
        int readyReplicas = 1) =>
        $$"""
          {
            "apiVersion": "apps/v1",
            "kind": "Deployment",
            "metadata": {
              "name": "demo",
              "namespace": "demo",
              "uid": "uid-{{resourceVersion}}",
              "resourceVersion": "{{resourceVersion}}",
              "creationTimestamp": "2026-05-07T00:00:00Z",
              "generation": {{resourceVersion}},
              "annotations": {
                "kubectl.kubernetes.io/last-applied-configuration": "noisy"
              },
              "managedFields": [{ "manager": "kubectl" }]
            },
            "spec": {
              "replicas": 1,
              "selector": { "matchLabels": { "app": "demo" } },
              "template": {
                "metadata": { "labels": { "app": "demo" } },
                "spec": {
                  "containers": [
                    { "name": "nginx", "image": "{{image}}" }
                  ]
                }
              }
            },
            "status": {
              "readyReplicas": {{readyReplicas}}
            }
          }
          """;

    private static string ConfigMapJson(string value, string resourceVersion) =>
        $$"""
          {
            "apiVersion": "v1",
            "kind": "ConfigMap",
            "metadata": {
              "name": "demo-config",
              "namespace": "demo",
              "resourceVersion": "{{resourceVersion}}",
              "managedFields": [{ "manager": "kubectl" }]
            },
            "data": {
              "hello": "{{value}}"
            }
          }
          """;
}
