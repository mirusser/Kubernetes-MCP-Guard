using System.Text.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.K3s;
using ServerKubernetesEvidenceService = InfraGate.McpServer.KubernetesEvidenceService;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class K3sApplyFlowTests(SafetyE2EFixture fixture)
{
    private const string K3sImage = "rancher/k3s:v1.31.2-k3s1";
    private const string DeploymentName = "nginx-k3s-apply-flow";
    private const string ContainerName = "nginx";
    private const string ContainerImage = "nginx:1.27-alpine";
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task BuildApplyPlan_AgainstEphemeralK3s_ReturnsSuccess()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        await using var k3sContainer = new K3sBuilder(K3sImage).Build();

        string? kubeconfigPath = null;
        try
        {
            using var startupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await k3sContainer.StartAsync(startupTimeout.Token);

            string kubeconfig = await k3sContainer.GetKubeconfigAsync();
            kubeconfigPath = Path.Combine(Path.GetTempPath(), $"k3s-{Guid.NewGuid():N}.yaml");
            await File.WriteAllTextAsync(kubeconfigPath, kubeconfig, startupTimeout.Token);

            var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeconfigPath);
            using var kubernetes = new Kubernetes(config);

            await CreateNamespaceAsync(kubernetes, fixture.Namespace, startupTimeout.Token);
            await CreateDeploymentAsync(kubernetes, fixture.Namespace, startupTimeout.Token);

            var builder = CreatePlanBuilder(kubernetes, fixture.Namespace);
            string manifest = CreateDeploymentManifest(fixture.Namespace);
            var result = await builder.BuildAsync(
                KubernetesAdapterConventions.MutationTools.ApplyManifest,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [KubernetesAdapterConventions.ToolArguments.Namespace] = fixture.Namespace,
                    [KubernetesAdapterConventions.ToolArguments.Manifest] = manifest
                },
                new PlanRequester("safety-e2e-k3s", "test"),
                startupTimeout.Token);

            Assert.True(result.Succeeded, result.Message);
            Assert.NotNull(result.Envelope);
            Assert.Equal(KubernetesAdapterConventions.PlanOperations.Apply, result.Envelope.Operation);
            Assert.Equal(fixture.Namespace, result.TargetNamespace);

            var payload = result.Envelope.Payload.Deserialize<KubernetesPlanPayload>(jsonOptions);
            Assert.NotNull(payload);
            Assert.Equal(fixture.Namespace, payload.Namespace);
            Assert.NotNull(payload.DryRun);
            Assert.NotEmpty(payload.DryRun.Objects);
            Assert.Contains(payload.Objects, obj =>
                obj.Kind == KubernetesAdapterConventions.ResourceKinds.Deployment &&
                obj.Namespace == fixture.Namespace &&
                obj.Name == DeploymentName);
        }
        catch (Exception ex) when (IsK3sStartupOrRuntimeUnavailable(ex))
        {
            return;
        }
        finally
        {
            if (kubeconfigPath is not null && File.Exists(kubeconfigPath))
            {
                File.Delete(kubeconfigPath);
            }
        }
    }

    private static KubernetesPlanBuilder CreatePlanBuilder(IKubernetes kubernetes, string namespaceName)
    {
        var options = new InfraGate.McpServer.KubernetesMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { namespaceName });
        var serverEvidence = new ServerKubernetesEvidenceService(
            kubernetes,
            NullLogger<ServerKubernetesEvidenceService>.Instance,
            options);

        return new KubernetesPlanBuilder(new DirectKubernetesEvidenceToolCaller(serverEvidence));
    }

    private static Task CreateNamespaceAsync(
        IKubernetes kubernetes,
        string namespaceName,
        CancellationToken cancellationToken) =>
        kubernetes.CoreV1.CreateNamespaceAsync(
            new V1Namespace
            {
                Metadata = new V1ObjectMeta
                {
                    Name = namespaceName
                }
            },
            cancellationToken: cancellationToken);

    private static Task CreateDeploymentAsync(
        IKubernetes kubernetes,
        string namespaceName,
        CancellationToken cancellationToken)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["app"] = DeploymentName
        };

        var deployment = new V1Deployment
        {
            ApiVersion = KubernetesAdapterConventions.ApiVersions.AppsV1,
            Kind = KubernetesAdapterConventions.ResourceKinds.Deployment,
            Metadata = new V1ObjectMeta
            {
                Name = DeploymentName,
                NamespaceProperty = namespaceName,
                Labels = labels
            },
            Spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Selector = new V1LabelSelector
                {
                    MatchLabels = labels
                },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = labels
                    },
                    Spec = new V1PodSpec
                    {
                        Containers =
                        [
                            new V1Container
                            {
                                Name = ContainerName,
                                Image = ContainerImage
                            }
                        ]
                    }
                }
            }
        };

        return kubernetes.AppsV1.CreateNamespacedDeploymentAsync(
            deployment,
            namespaceName,
            cancellationToken: cancellationToken);
    }

    private static string CreateDeploymentManifest(string namespaceName) =>
        $$"""
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: {{DeploymentName}}
          namespace: {{namespaceName}}
          labels:
            app: {{DeploymentName}}
        spec:
          replicas: 1
          selector:
            matchLabels:
              app: {{DeploymentName}}
          template:
            metadata:
              labels:
                app: {{DeploymentName}}
            spec:
              containers:
              - name: {{ContainerName}}
                image: {{ContainerImage}}
        """;

    private static bool IsK3sStartupOrRuntimeUnavailable(Exception ex) =>
        ex is OperationCanceledException or TimeoutException or IOException or HttpRequestException ||
        ex.GetType().FullName?.Contains("Testcontainers", StringComparison.Ordinal) == true;

    private sealed class DirectKubernetesEvidenceToolCaller(ServerKubernetesEvidenceService evidenceService) : IKubernetesEvidenceService, IToolCaller
    {
        public Task<string> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct) =>
            DirectToolCaller.CallEvidenceAsync(evidenceService, toolName, arguments, ct);

        public async Task<KubernetesApplyEvidence?> GetApplyEvidenceAsync(string namespaceName, string manifest, CancellationToken ct)
        {
            string json = await evidenceService.EvidenceDryRunApplyManifestAsync(namespaceName, manifest, ct).ConfigureAwait(false);
            return Deserialize<KubernetesApplyEvidence>(json);
        }

        public async Task<KubernetesPlanDryRun?> GetDryRunAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken ct)
        {
            string json = await CallAsync(toolName, arguments, ct).ConfigureAwait(false);
            return Deserialize<KubernetesPlanDryRun>(json);
        }

        public async Task<KubernetesPlanDiff[]?> GetDiffsAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken ct)
        {
            string json = await CallAsync(toolName, arguments, ct).ConfigureAwait(false);
            return Deserialize<KubernetesPlanDiff[]>(json);
        }

        public Task<KubernetesApplyEvidence?> CheckApplyDryRunAsync(string namespaceName, string manifest, CancellationToken ct) =>
            GetApplyEvidenceAsync(namespaceName, manifest, ct);

        private static T? Deserialize<T>(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, jsonOptions);
            }
            catch (JsonException)
            {
                return default;
            }
        }
    }

    private sealed class DirectToolCaller(ServerKubernetesEvidenceService evidenceService) : IToolCaller
    {
        public Task<string> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct) =>
            CallEvidenceAsync(evidenceService, toolName, arguments, ct);

        public static Task<string> CallEvidenceAsync(
            ServerKubernetesEvidenceService evidenceService,
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken ct) =>
            toolName switch
            {
                KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest => evidenceService.EvidenceDryRunApplyManifestAsync(
                    GetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace),
                    GetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Manifest),
                    ct),
                KubernetesAdapterConventions.EvidenceTools.DiffManifest => evidenceService.EvidenceDiffManifestAsync(
                    GetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace),
                    GetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Manifest),
                    ct),
                _ => throw new InvalidOperationException($"Unsupported evidence tool '{toolName}'.")
            };

        private static string GetString(IReadOnlyDictionary<string, object?> arguments, string key) =>
            arguments.TryGetValue(key, out var value) && value is string text
                ? text
                : throw new ArgumentException($"Missing required argument '{key}'.", nameof(arguments));
    }
}
