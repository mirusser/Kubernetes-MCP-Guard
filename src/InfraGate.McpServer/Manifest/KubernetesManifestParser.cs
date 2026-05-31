using InfraGate.McpServer.Models;
using k8s;
using k8s.Models;

namespace InfraGate.McpServer;

public static class KubernetesManifestParser
{
    private static readonly Dictionary<string, Type> TypeMap = new(StringComparer.Ordinal)
    {
        [KubernetesConventions.KubernetesResources.DeploymentTypeKey] = typeof(V1Deployment),
        [KubernetesConventions.KubernetesResources.ServiceTypeKey] = typeof(V1Service),
        [KubernetesConventions.KubernetesResources.ConfigMapTypeKey] = typeof(V1ConfigMap)
    };

    public static KubernetesParsedManifest ParseSupported(string manifest, string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(manifest))
        {
            throw new KubernetesValidationException("Manifest is required.");
        }

        object[] objects;
        try
        {
            objects = KubernetesYaml.LoadAllFromString(manifest, TypeMap, strict: true)
                .Where(obj => obj is not null)
                .ToArray();
        }
        catch (Exception ex)
        {
            throw new KubernetesValidationException($"Manifest could not be parsed: {ex.Message}");
        }

        if (objects.Length == 0)
        {
            throw new KubernetesValidationException("Manifest must contain at least one Kubernetes object.");
        }

        var supportedObjects = new List<IKubernetesObject<V1ObjectMeta>>();
        var objectRefs = new List<KubernetesObjectRef>();

        foreach (var obj in objects)
        {
            var supportedObject = obj switch
            {
                V1Deployment deployment => (IKubernetesObject<V1ObjectMeta>)ValidateAndPrepare(
                    deployment,
                    KubernetesConventions.KubernetesResources.AppsV1,
                    KubernetesConventions.KubernetesResources.Deployment,
                    namespaceName),
                V1Service service => ValidateAndPrepare(
                    service,
                    KubernetesConventions.KubernetesResources.V1,
                    KubernetesConventions.KubernetesResources.Service,
                    namespaceName),
                V1ConfigMap configMap => ValidateAndPrepare(
                    configMap,
                    KubernetesConventions.KubernetesResources.V1,
                    KubernetesConventions.KubernetesResources.ConfigMap,
                    namespaceName),
                IKubernetesObject<V1ObjectMeta> kubernetesObject => throw new KubernetesValidationException(
                    $"Unsupported Kubernetes kind '{kubernetesObject.ApiVersion}/{kubernetesObject.Kind}'. Supported kinds: {KubernetesConventions.KubernetesResources.SupportedKindsDescription}."),
                _ => throw new KubernetesValidationException("Manifest contains an unsupported Kubernetes document.")
            };

            supportedObjects.Add(supportedObject);
            objectRefs.Add(new KubernetesObjectRef(
                supportedObject.ApiVersion,
                supportedObject.Kind,
                supportedObject.Metadata.NamespaceProperty,
                supportedObject.Metadata.Name));
        }

        return new KubernetesParsedManifest(supportedObjects, objectRefs.ToArray());
    }

    private static T ValidateAndPrepare<T>(T obj, string apiVersion, string kind, string namespaceName)
        where T : IKubernetesObject<V1ObjectMeta>
    {
        if (!string.Equals(obj.ApiVersion, apiVersion, StringComparison.Ordinal) ||
            !string.Equals(obj.Kind, kind, StringComparison.Ordinal))
        {
            throw new KubernetesValidationException($"Manifest object must declare apiVersion '{apiVersion}' and kind '{kind}'.");
        }

        if (obj.Metadata is null)
        {
            throw new KubernetesValidationException($"{apiVersion}/{kind} is missing metadata.");
        }

        if (string.IsNullOrWhiteSpace(obj.Metadata.Name))
        {
            throw new KubernetesValidationException($"{apiVersion}/{kind} is missing metadata.name.");
        }

        if (string.IsNullOrWhiteSpace(obj.Metadata.NamespaceProperty))
        {
            obj.Metadata.NamespaceProperty = namespaceName;
        }
        else if (!string.Equals(obj.Metadata.NamespaceProperty, namespaceName, StringComparison.Ordinal))
        {
            throw new KubernetesValidationException(
                $"{apiVersion}/{kind} '{obj.Metadata.Name}' targets namespace '{obj.Metadata.NamespaceProperty}', but the tool namespace is '{namespaceName}'.");
        }

        return obj;
    }
}
