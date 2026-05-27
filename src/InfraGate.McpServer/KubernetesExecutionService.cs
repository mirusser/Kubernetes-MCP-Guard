using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.Policy;
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

        var policyResult = KubernetesPolicyValidator.Validate(parsed.Objects, KubernetesPolicyOptions.Default);
        if (policyResult.IsDenied)
        {
            return $"Apply refused by policy:{Environment.NewLine}{policyResult.FormatRefusal()}";
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

            messages.Add($"Applied {obj.ApiVersion} {obj.Kind} {obj.Metadata.NamespaceProperty}/{obj.Metadata.Name}");
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

        var restartedAtUtc = DateTimeOffset.UtcNow.ToString(ApprovalConventions.DateTimeFormats.RoundTrip);

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
}
