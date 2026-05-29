using InfraGate.McpServer.Models;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer;

public sealed class KubernetesExecutionService
{
    private readonly IKubernetes client;
    private readonly ILogger<KubernetesExecutionService> logger;
    private readonly KubernetesMcpOptions options;

    public KubernetesExecutionService(IKubernetes client, ILogger<KubernetesExecutionService> logger, KubernetesMcpOptions options)
    {
        this.client = client;
        this.logger = logger;
        this.options = options;
    }

    public async Task<string> ExecuteApplyManifestAsync(
        string namespaceName,
        string manifest,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        KubernetesParsedManifest parsed;
        try
        {
            parsed = KubernetesManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (KubernetesValidationException ex)
        {
            return ex.Message;
        }

        var policyRefusal = CheckDenyPolicy(parsed.Objects);
        if (policyRefusal is not null)
        {
            return policyRefusal;
        }

        var messages = new List<string>();
        foreach (var obj in parsed.Objects)
        {
            try
            {
                await ApplyObjectAsync(obj, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Apply failed for {ApiVersion} {Kind} {Namespace}/{Name}",
                    obj.ApiVersion, obj.Kind, obj.Metadata.NamespaceProperty, obj.Metadata.Name);
                return KubernetesManagerHelpers.FormatServerSideApplyException("Apply failed", ex);
            }

            messages.Add($"{KubernetesConventions.ExecutionMessages.ApplySuccess} {obj.ApiVersion} {obj.Kind} {obj.Metadata.NamespaceProperty}/{obj.Metadata.Name}");
        }

        return string.Join(Environment.NewLine, messages);
    }

    public async Task<string> ExecuteDeleteManifestAsync(
        string namespaceName,
        string manifest,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        KubernetesParsedManifest parsed;
        try
        {
            parsed = KubernetesManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (KubernetesValidationException ex)
        {
            return ex.Message;
        }

        var messages = new List<string>();
        foreach (var obj in parsed.ObjectRefs)
        {
            messages.Add(await DeleteObjectAsync(obj, cancellationToken).ConfigureAwait(false));
        }

        return string.Join(Environment.NewLine, messages);
    }

    public async Task<string> ExecuteScaleDeploymentAsync(
        string namespaceName,
        string name,
        int replicas,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName) ?? KubernetesManagerHelpers.ValidateName(name) ?? KubernetesManagerHelpers.ValidateReplicas(replicas);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            await client.AppsV1.PatchNamespacedDeploymentScaleAsync(
                CreateScaleDeploymentPatch(replicas),
                name,
                namespaceName,
                fieldManager: KubernetesManagerHelpers.FieldManager,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return $"Scaled {KubernetesConventions.KubernetesResources.DeploymentDisplayName} {namespaceName}/{name} to {replicas} replicas.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scale failed for {Namespace}/{Name}", namespaceName, name);
            return KubernetesManagerHelpers.FormatApiException("Scale failed", ex);
        }
    }

    public async Task<string> ExecuteRestartDeploymentAsync(
        string namespaceName,
        string name,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName) ?? KubernetesManagerHelpers.ValidateName(name);
        if (validation is not null)
        {
            return validation;
        }

        var restartedAtUtc = DateTimeOffset.UtcNow.ToString(KubernetesConventions.DateTimeFormats.RoundTrip);

        try
        {
            await client.AppsV1.PatchNamespacedDeploymentAsync(
                CreateRestartDeploymentPatch(restartedAtUtc),
                name,
                namespaceName,
                fieldManager: KubernetesManagerHelpers.FieldManager,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return $"Restarted {KubernetesConventions.KubernetesResources.DeploymentDisplayName} {namespaceName}/{name} at {restartedAtUtc}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Restart failed for {Namespace}/{Name}", namespaceName, name);
            return KubernetesManagerHelpers.FormatApiException("Restart failed", ex);
        }
    }

    public async Task<string> ExecuteSetDeploymentImageAsync(
        string namespaceName,
        string name,
        string container,
        string image,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName) ??
            KubernetesManagerHelpers.ValidateName(name) ??
            KubernetesManagerHelpers.ValidateRequiredText(container, "Container name") ??
            KubernetesManagerHelpers.ValidateRequiredText(image, "Image");
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            await client.AppsV1.PatchNamespacedDeploymentAsync(
                CreateSetDeploymentImagePatch(container, image),
                name,
                namespaceName,
                fieldManager: KubernetesManagerHelpers.FieldManager,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return $"Updated {KubernetesConventions.KubernetesResources.DeploymentDisplayName} {namespaceName}/{name} container '{container}' image to '{image}'.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Set image failed for {Namespace}/{Name}", namespaceName, name);
            return KubernetesManagerHelpers.FormatApiException("Set image failed", ex);
        }
    }

    private async Task ApplyObjectAsync(IKubernetesObject<V1ObjectMeta> obj, CancellationToken cancellationToken)
    {
        if (await TryApplyDeploymentAsync(obj, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (await TryApplyServiceAsync(obj, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (await TryApplyConfigMapAsync(obj, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        throw new InvalidOperationException("Unsupported object for server-side apply.");
    }

    private async Task<bool> TryApplyDeploymentAsync(
        IKubernetesObject<V1ObjectMeta> obj,
        CancellationToken cancellationToken)
    {
        if (obj is not V1Deployment deployment)
        {
            return false;
        }

        await ApplyDeploymentAsync(deployment, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryApplyServiceAsync(
        IKubernetesObject<V1ObjectMeta> obj,
        CancellationToken cancellationToken)
    {
        if (obj is not V1Service service)
        {
            return false;
        }

        await ApplyServiceAsync(service, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryApplyConfigMapAsync(
        IKubernetesObject<V1ObjectMeta> obj,
        CancellationToken cancellationToken)
    {
        if (obj is not V1ConfigMap configMap)
        {
            return false;
        }

        await ApplyConfigMapAsync(configMap, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Task<V1Deployment> ApplyDeploymentAsync(V1Deployment deployment, CancellationToken cancellationToken) =>
        client.AppsV1.PatchNamespacedDeploymentAsync(
            new V1Patch(deployment, V1Patch.PatchType.ApplyPatch),
            deployment.Metadata.Name,
            deployment.Metadata.NamespaceProperty,
            fieldManager: KubernetesManagerHelpers.FieldManager,
            cancellationToken: cancellationToken);

    private Task<V1Service> ApplyServiceAsync(V1Service service, CancellationToken cancellationToken) =>
        client.CoreV1.PatchNamespacedServiceAsync(
            new V1Patch(service, V1Patch.PatchType.ApplyPatch),
            service.Metadata.Name,
            service.Metadata.NamespaceProperty,
            fieldManager: KubernetesManagerHelpers.FieldManager,
            cancellationToken: cancellationToken);

    private Task<V1ConfigMap> ApplyConfigMapAsync(V1ConfigMap configMap, CancellationToken cancellationToken) =>
        client.CoreV1.PatchNamespacedConfigMapAsync(
            new V1Patch(configMap, V1Patch.PatchType.ApplyPatch),
            configMap.Metadata.Name,
            configMap.Metadata.NamespaceProperty,
            fieldManager: KubernetesManagerHelpers.FieldManager,
            cancellationToken: cancellationToken);

    private async Task<string> DeleteObjectAsync(KubernetesObjectRef obj, CancellationToken cancellationToken)
    {
        try
        {
            switch (obj.ApiVersion, obj.Kind)
            {
                case (KubernetesConventions.KubernetesResources.AppsV1, KubernetesConventions.KubernetesResources.Deployment):
                    await client.AppsV1.DeleteNamespacedDeploymentAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                case (KubernetesConventions.KubernetesResources.V1, KubernetesConventions.KubernetesResources.Service):
                    await client.CoreV1.DeleteNamespacedServiceAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                case (KubernetesConventions.KubernetesResources.V1, KubernetesConventions.KubernetesResources.ConfigMap):
                    await client.CoreV1.DeleteNamespacedConfigMapAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    return $"Skipped unsupported {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}.";
            }

            return $"Deleted {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";
        }
        catch (Exception ex) when (KubernetesManagerHelpers.IsNotFound(ex))
        {
            return $"Skipped missing {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";
        }
    }

    private static V1Patch CreateScaleDeploymentPatch(int replicas) =>
        new(new
        {
            spec = new
            {
                replicas
            }
        }, V1Patch.PatchType.MergePatch);

    private static V1Patch CreateRestartDeploymentPatch(string restartedAtUtc) =>
        new(new
        {
            spec = new
            {
                template = new
                {
                    metadata = new
                    {
                        annotations = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [KubernetesManagerHelpers.RestartedAtAnnotation] = restartedAtUtc
                        }
                    }
                }
            }
        }, V1Patch.PatchType.StrategicMergePatch);

    private static V1Patch CreateSetDeploymentImagePatch(string container, string image) =>
        new(new
        {
            spec = new
            {
                template = new
                {
                    spec = new
                    {
                        containers = new[]
                        {
                            new
                            {
                                name = container,
                                image
                            }
                        }
                    }
                }
            }
        }, V1Patch.PatchType.StrategicMergePatch);

    private static string? CheckDenyPolicy(IReadOnlyList<IKubernetesObject<V1ObjectMeta>> objects)
    {
        var denials = new List<string>();

        foreach (var obj in objects)
        {
            if (obj is V1Deployment deployment)
            {
                CheckDeploymentDenyRules(deployment, denials);
            }
            else if (obj is V1Service service)
            {
                CheckServiceDenyRules(service, denials);
            }
        }

        return denials.Count > 0
            ? $"{KubernetesConventions.ExecutionMessages.PolicyRefusal}{Environment.NewLine}{string.Join(Environment.NewLine, denials)}"
            : null;
    }

    private static void CheckDeploymentDenyRules(V1Deployment deployment, List<string> denials)
    {
        var podSpec = deployment.Spec?.Template?.Spec;
        if (podSpec is null) return;

        var objRef = $"apps/v1 Deployment {deployment.Metadata.NamespaceProperty}/{deployment.Metadata.Name}";

        if (podSpec.HostNetwork == true)
            denials.Add($"  [{KubernetesConventions.PolicyCodes.DeploymentHostNamespace}] hostNetwork is not allowed. ({objRef})");
        if (podSpec.HostPID == true)
            denials.Add($"  [{KubernetesConventions.PolicyCodes.DeploymentHostNamespace}] hostPID is not allowed. ({objRef})");
        if (podSpec.HostIPC == true)
            denials.Add($"  [{KubernetesConventions.PolicyCodes.DeploymentHostNamespace}] hostIPC is not allowed. ({objRef})");

        foreach (var volume in (podSpec.Volumes ?? []).Where(v => v.HostPath is not null))
            denials.Add($"  [{KubernetesConventions.PolicyCodes.DeploymentHostPath}] Volume '{volume.Name}' uses hostPath, which is not allowed. ({objRef})");

        var allContainers = (podSpec.Containers ?? []).Concat(podSpec.InitContainers ?? []);
        foreach (var container in allContainers)
        {
            if (container.SecurityContext?.Privileged == true)
                denials.Add($"  [{KubernetesConventions.PolicyCodes.DeploymentPrivilegedContainer}] Container '{container.Name}' is privileged, which is not allowed. ({objRef})");

            var caps = container.SecurityContext?.Capabilities?.Add;
            if (caps?.Count > 0)
                denials.Add($"  [{KubernetesConventions.PolicyCodes.DeploymentAddedCapabilities}] Container '{container.Name}' adds Linux capabilities [{string.Join(", ", caps)}], which is not allowed. ({objRef})");

            if (IsLatestOrImplicitImageTag(container.Image))
                denials.Add($"  [{KubernetesConventions.PolicyCodes.ImageLatestTag}] Container '{container.Name}' uses image '{container.Image}' which resolves to latest. Pin to a specific tag. ({objRef})");
        }
    }

    private static void CheckServiceDenyRules(V1Service service, List<string> denials)
    {
        var type = service.Spec?.Type;
        var objRef = $"v1 Service {service.Metadata.NamespaceProperty}/{service.Metadata.Name}";

        if (string.Equals(type, "LoadBalancer", StringComparison.Ordinal))
            denials.Add($"  [{KubernetesConventions.PolicyCodes.ServiceLoadBalancer}] Service type LoadBalancer is not allowed. ({objRef})");
        if (string.Equals(type, "NodePort", StringComparison.Ordinal))
            denials.Add($"  [{KubernetesConventions.PolicyCodes.ServiceNodePort}] Service type NodePort is not allowed. ({objRef})");
    }

    private static bool IsLatestOrImplicitImageTag(string? image) =>
        string.IsNullOrEmpty(image) ||
        !image.Contains(':', StringComparison.Ordinal) ||
        image.EndsWith(":latest", StringComparison.Ordinal);
}
