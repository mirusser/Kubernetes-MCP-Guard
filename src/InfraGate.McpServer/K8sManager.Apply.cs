using InfraGate.KubernetesAdapter;
using k8s;
using k8s.Models;

namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed partial class K8sManager
{
    private async Task ApplyObjectAsync(IKubernetesObject<V1ObjectMeta> obj, CancellationToken cancellationToken)
    {
        if (await TryApplyDeploymentAsync(obj, cancellationToken))
        {
            return;
        }

        if (await TryApplyServiceAsync(obj, cancellationToken))
        {
            return;
        }

        await TryApplyConfigMapAsync(obj, cancellationToken);
    }

    private async Task<bool> TryApplyDeploymentAsync(
        IKubernetesObject<V1ObjectMeta> obj,
        CancellationToken cancellationToken)
    {
        if (obj is not V1Deployment deployment)
        {
            return false;
        }

        await ApplyDeploymentAsync(deployment, cancellationToken);
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

        await ApplyServiceAsync(service, cancellationToken);
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

        await ApplyConfigMapAsync(configMap, cancellationToken);
        return true;
    }

    private Task<V1Deployment> ApplyDeploymentAsync(V1Deployment deployment, CancellationToken cancellationToken) =>
        client.AppsV1.PatchNamespacedDeploymentAsync(
            new V1Patch(deployment, V1Patch.PatchType.ApplyPatch),
            deployment.Metadata.Name,
            deployment.Metadata.NamespaceProperty,
            fieldManager: FieldManager,
            cancellationToken: cancellationToken);

    private Task<V1Service> ApplyServiceAsync(V1Service service, CancellationToken cancellationToken) =>
        client.CoreV1.PatchNamespacedServiceAsync(
            new V1Patch(service, V1Patch.PatchType.ApplyPatch),
            service.Metadata.Name,
            service.Metadata.NamespaceProperty,
            fieldManager: FieldManager,
            cancellationToken: cancellationToken);

    private Task<V1ConfigMap> ApplyConfigMapAsync(V1ConfigMap configMap, CancellationToken cancellationToken) =>
        client.CoreV1.PatchNamespacedConfigMapAsync(
            new V1Patch(configMap, V1Patch.PatchType.ApplyPatch),
            configMap.Metadata.Name,
            configMap.Metadata.NamespaceProperty,
            fieldManager: FieldManager,
            cancellationToken: cancellationToken);

    private async Task<string> DeleteObjectAsync(K8sObjectRef obj, CancellationToken cancellationToken)
    {
        try
        {
            switch (obj.ApiVersion, obj.Kind)
            {
                case (K8sConventions.K8sResources.AppsV1, K8sConventions.K8sResources.Deployment):
                    await client.AppsV1.DeleteNamespacedDeploymentAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken);
                    break;
                case (K8sConventions.K8sResources.V1, K8sConventions.K8sResources.Service):
                    await client.CoreV1.DeleteNamespacedServiceAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken);
                    break;
                case (K8sConventions.K8sResources.V1, K8sConventions.K8sResources.ConfigMap):
                    await client.CoreV1.DeleteNamespacedConfigMapAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken);
                    break;
                default:
                    return $"Skipped unsupported {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}.";
            }

            return $"Deleted {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return $"Skipped missing {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";
        }
    }
}
