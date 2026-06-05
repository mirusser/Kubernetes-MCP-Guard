using InfraGate.McpServer;
using k8s;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesExecutionServiceTests
{
    private const string DemoNamespace = "demo";

    [Theory]
    [InlineData(PrivilegedDeploymentManifest, KubernetesConventions.PolicyCodes.DeploymentPrivilegedContainer)]
    [InlineData(HostNetworkDeploymentManifest, KubernetesConventions.PolicyCodes.DeploymentHostNamespace)]
    [InlineData(LoadBalancerServiceManifest, KubernetesConventions.PolicyCodes.ServiceLoadBalancer)]
    public async Task ExecuteApplyManifestAsync_PolicyViolation_ReturnsPolicyRefusal(
        string manifest, string expectedCode)
    {
        await using var api = new TestKubernetesApi(_ =>
            throw new InvalidOperationException("API must not be called when policy denies"));
        var service = CreateService(api);

        var result = await service.ExecuteApplyManifestAsync(DemoNamespace, manifest, resourceVersionsJson: null, CancellationToken.None);

        Assert.StartsWith(KubernetesConventions.ExecutionMessages.PolicyRefusal, result, StringComparison.Ordinal);
        Assert.Contains(expectedCode, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteApplyManifestAsync_SafeDeployment_AppliesObject()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(SafeDeploymentResponse()));
        var service = CreateService(api);

        var result = await service.ExecuteApplyManifestAsync(DemoNamespace, SafeDeploymentManifest, resourceVersionsJson: null, CancellationToken.None);

        Assert.DoesNotContain(KubernetesConventions.ExecutionMessages.PolicyRefusal, result, StringComparison.Ordinal);
        Assert.Contains(KubernetesConventions.ExecutionMessages.ApplySuccess, result, StringComparison.Ordinal);
    }

    private static KubernetesExecutionService CreateService(TestKubernetesApi api)
    {
        var options = new KubernetesMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { DemoNamespace });
        var client = new Kubernetes(new KubernetesClientConfiguration
        {
            Host = api.Url,
            SkipTlsVerify = true
        });
        return new KubernetesExecutionService(client, NullLogger<KubernetesExecutionService>.Instance, options);
    }

    private const string PrivilegedDeploymentManifest = """
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
                securityContext:
                  privileged: true
        """;

    private const string HostNetworkDeploymentManifest = """
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
              hostNetwork: true
              containers:
              - name: app
                image: nginx:1.27-alpine
        """;

    private const string LoadBalancerServiceManifest = """
        apiVersion: v1
        kind: Service
        metadata:
          name: nginx-svc
          namespace: demo
        spec:
          type: LoadBalancer
          selector:
            app: nginx
          ports:
          - port: 80
            targetPort: 80
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

    [Fact]
    public async Task ExecuteDeleteManifestAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.ExecuteDeleteManifestAsync("disallowed", SafeDeploymentManifest, CancellationToken.None);

        Assert.Contains("disallowed", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteScaleDeploymentAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.ExecuteScaleDeploymentAsync("disallowed", "app", 3, CancellationToken.None);

        Assert.Contains("disallowed", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteRestartDeploymentAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.ExecuteRestartDeploymentAsync("disallowed", "app", CancellationToken.None);

        Assert.Contains("disallowed", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteSetDeploymentImageAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.ExecuteSetDeploymentImageAsync("disallowed", "app", "container", "image:v2", CancellationToken.None);

        Assert.Contains("disallowed", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteDeleteManifestAsync_InvalidManifest_ReturnsParseError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.ExecuteDeleteManifestAsync(DemoNamespace, "bad-yaml", CancellationToken.None);

        Assert.DoesNotContain("should not call", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteApplyManifestAsync_DisallowedNamespace_ReturnsValidationError()
    {
        await using var api = new TestKubernetesApi(_ => throw new InvalidOperationException("should not call k8s"));
        var service = CreateService(api);

        var result = await service.ExecuteApplyManifestAsync("disallowed", SafeDeploymentManifest, resourceVersionsJson: null, CancellationToken.None);

        Assert.Contains("disallowed", result, StringComparison.Ordinal);
    }

    private static string SafeDeploymentResponse() =>
        """
        {
          "apiVersion": "apps/v1",
          "kind": "Deployment",
          "metadata": { "name": "nginx", "namespace": "demo" },
          "spec": {
            "replicas": 1,
            "selector": { "matchLabels": { "app": "nginx" } },
            "template": {
              "metadata": { "labels": { "app": "nginx" } },
              "spec": { "containers": [{ "name": "app", "image": "nginx:1.27-alpine" }] }
            }
          }
        }
        """;
}
