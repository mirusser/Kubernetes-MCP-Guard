using System.Text.Json;
using InfraGate.McpServer;
using k8s;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesEvidenceServiceTests
{
    private const string DemoNamespace = "demo";
    private const string DisallowedNamespace = "disallowed";

    [Fact]
    public async Task EvidenceDryRunApplyManifestAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceDryRunApplyManifestAsync(DisallowedNamespace, SafeDeploymentManifest, CancellationToken.None);

        Assert.Contains(DisallowedNamespace, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceDryRunApplyManifestAsync_InvalidManifest_ReturnsParseError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceDryRunApplyManifestAsync(DemoNamespace, "not a valid manifest", CancellationToken.None);

        Assert.DoesNotContain("should not call", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceDryRunDeleteManifestAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceDryRunDeleteManifestAsync(DisallowedNamespace, SafeDeploymentManifest, CancellationToken.None);

        Assert.Contains(DisallowedNamespace, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceDryRunDeleteManifestAsync_InvalidManifest_ReturnsParseError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceDryRunDeleteManifestAsync(DemoNamespace, "invalid", CancellationToken.None);

        Assert.DoesNotContain("should not call", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceDryRunScaleDeploymentAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceDryRunScaleDeploymentAsync(DisallowedNamespace, "app", 3, CancellationToken.None);

        Assert.Contains(DisallowedNamespace, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceDryRunRestartDeploymentAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceDryRunRestartDeploymentAsync(DisallowedNamespace, "app", CancellationToken.None);

        Assert.Contains(DisallowedNamespace, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceDryRunSetDeploymentImageAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceDryRunSetDeploymentImageAsync(DisallowedNamespace, "app", "container", "image:v2", CancellationToken.None);

        Assert.Contains(DisallowedNamespace, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceCheckLiveDriftAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceCheckLiveDriftAsync(DisallowedNamespace, "scale", "[{\"kind\":\"Deployment\"}]", CancellationToken.None);

        Assert.Contains(DisallowedNamespace, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceCheckLiveDriftAsync_InvalidJson_ReturnsParseError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceCheckLiveDriftAsync(DemoNamespace, "scale", "not-valid-json", CancellationToken.None);

        Assert.Contains("Could not parse diffs", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceCheckLiveDriftAsync_EmptyDiffsArray_ReturnsEmptyMessage()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceCheckLiveDriftAsync(DemoNamespace, "scale", "[]", CancellationToken.None);

        Assert.Contains("empty", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvidenceCheckResourceVersionAsync_Match_ReturnsOk()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/nginx" => TestResponse.Json(DeploymentResponseWithVersion("42")),
            _ => TestResponse.Json("{}")
        });
        var service = CreateService(api);
        var resourceVersionsJson = JsonSerializer.Serialize(
            new Dictionary<string, string> { ["apps/v1 Deployment demo/nginx"] = "42" });

        var result = await service.EvidenceCheckResourceVersionAsync(
            DemoNamespace, resourceVersionsJson, CancellationToken.None);

        Assert.Equal(KubernetesConventions.DriftCheckResult.NoDrift, result);
    }

    [Fact]
    public async Task EvidenceCheckResourceVersionAsync_Mismatch_ReturnsError()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/nginx" => TestResponse.Json(DeploymentResponseWithVersion("99")),
            _ => TestResponse.Json("{}")
        });
        var service = CreateService(api);
        var resourceVersionsJson = JsonSerializer.Serialize(
            new Dictionary<string, string> { ["apps/v1 Deployment demo/nginx"] = "42" });

        var result = await service.EvidenceCheckResourceVersionAsync(
            DemoNamespace, resourceVersionsJson, CancellationToken.None);

        Assert.Contains("ResourceVersion mismatch", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvidenceCheckResourceVersionAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceCheckResourceVersionAsync(
            DisallowedNamespace, "{}", CancellationToken.None);

        Assert.Contains(DisallowedNamespace, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceCheckResourceVersionAsync_InvalidJson_ReturnsParseError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceCheckResourceVersionAsync(
            DemoNamespace, "not-valid-json", CancellationToken.None);

        Assert.Contains("Could not parse resource versions", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvidenceCheckResourceVersionAsync_EmptyDictionary_ReturnsOk()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceCheckResourceVersionAsync(
            DemoNamespace, "{}", CancellationToken.None);

        Assert.Equal(KubernetesConventions.DriftCheckResult.NoDrift, result);
    }

    [Fact]
    public async Task EvidenceDiffManifestAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceDiffManifestAsync(DisallowedNamespace, SafeDeploymentManifest, CancellationToken.None);

        Assert.Contains(DisallowedNamespace, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceDiffManifestAsync_InvalidManifest_ReturnsParseError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceDiffManifestAsync(DemoNamespace, "bad-yaml", CancellationToken.None);

        Assert.DoesNotContain("should not call", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceDiffDeploymentAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.EvidenceDiffDeploymentAsync(DisallowedNamespace, "app", "scale", 3, "container", "image:v2", CancellationToken.None);

        Assert.Contains(DisallowedNamespace, result, StringComparison.Ordinal);
    }

    private static KubernetesEvidenceService CreateService(TestKubernetesApi api)
    {
        var options = new KubernetesMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { DemoNamespace });
        var client = new Kubernetes(new KubernetesClientConfiguration
        {
            Host = api.Url,
            SkipTlsVerify = true
        });
        return new KubernetesEvidenceService(client, NullLogger<KubernetesEvidenceService>.Instance, options);
    }

    private static string SafeDeploymentResponse() => """
        {
            "apiVersion": "apps/v1",
            "kind": "Deployment",
            "metadata": {
                "name": "nginx",
                "namespace": "demo",
                "uid": "abc123"
            },
            "spec": {
                "replicas": 1,
                "selector": { "matchLabels": { "app": "nginx" } },
                "template": {
                    "metadata": { "labels": { "app": "nginx" } },
                    "spec": {
                        "containers": [
                            { "name": "app", "image": "nginx:1.27-alpine" }
                        ]
                    }
                }
            }
        }
        """;

    private static string DeploymentResponseWithVersion(string resourceVersion) => $$"""
        {
            "apiVersion": "apps/v1",
            "kind": "Deployment",
            "metadata": {
                "name": "nginx",
                "namespace": "demo",
                "resourceVersion": "{{resourceVersion}}",
                "generation": {{resourceVersion}}
            },
            "spec": {
                "replicas": 1,
                "selector": { "matchLabels": { "app": "nginx" } },
                "template": {
                    "metadata": { "labels": { "app": "nginx" } },
                    "spec": {
                        "containers": [
                            { "name": "app", "image": "nginx:1.27-alpine" }
                        ]
                    }
                }
            }
        }
        """;

    private const string SafeDeploymentManifest = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: nginx
          namespace: demo
        spec:
          replicas: 1
          selector:
            matchLabels:
              app: nginx
          template:
            metadata:
              labels:
                app: nginx
            spec:
              containers:
              - name: app
                image: nginx:1.27-alpine
        """;
}
