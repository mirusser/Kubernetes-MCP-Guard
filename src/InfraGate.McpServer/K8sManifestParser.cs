using k8s;
using k8s.Models;

namespace InfraGate.McpServer;

public static class K8sManifestParser
{
    private static readonly Dictionary<string, Type> TypeMap = new(StringComparer.Ordinal)
    {
        ["apps/v1/Deployment"] = typeof(V1Deployment),
        ["v1/Service"] = typeof(V1Service),
        ["v1/ConfigMap"] = typeof(V1ConfigMap)
    };

    public static K8sParsedManifest ParseSupported(string manifest, string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(manifest))
        {
            throw new K8sValidationException("Manifest is required.");
        }

        object[] objects;
        try
        {
            objects = KubernetesYaml.LoadAllFromString(manifest, TypeMap, strict: false)
                .Where(obj => obj is not null)
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new K8sValidationException($"Manifest could not be parsed: {ex.Message}");
        }

        if (objects.Length == 0)
        {
            throw new K8sValidationException("Manifest must contain at least one Kubernetes object.");
        }

        var supportedObjects = new List<IKubernetesObject<V1ObjectMeta>>();
        var objectRefs = new List<K8sObjectRef>();

        foreach (var obj in objects)
        {
            var supportedObject = obj switch
            {
                V1Deployment deployment => (IKubernetesObject<V1ObjectMeta>)ValidateAndPrepare(deployment, "apps/v1", "Deployment", namespaceName),
                V1Service service => ValidateAndPrepare(service, "v1", "Service", namespaceName),
                V1ConfigMap configMap => ValidateAndPrepare(configMap, "v1", "ConfigMap", namespaceName),
                IKubernetesObject<V1ObjectMeta> kubernetesObject => throw new K8sValidationException(
                    $"Unsupported Kubernetes kind '{kubernetesObject.ApiVersion}/{kubernetesObject.Kind}'. Supported kinds: apps/v1 Deployment, v1 Service, v1 ConfigMap."),
                _ => throw new K8sValidationException("Manifest contains an unsupported Kubernetes document.")
            };

            supportedObjects.Add(supportedObject);
            objectRefs.Add(new K8sObjectRef(
                supportedObject.ApiVersion,
                supportedObject.Kind,
                supportedObject.Metadata.NamespaceProperty,
                supportedObject.Metadata.Name));
        }

        return new K8sParsedManifest(supportedObjects, objectRefs.ToArray());
    }

    private static T ValidateAndPrepare<T>(T obj, string apiVersion, string kind, string namespaceName)
        where T : IKubernetesObject<V1ObjectMeta>
    {
        if (!string.Equals(obj.ApiVersion, apiVersion, StringComparison.Ordinal) ||
            !string.Equals(obj.Kind, kind, StringComparison.Ordinal))
        {
            throw new K8sValidationException($"Manifest object must declare apiVersion '{apiVersion}' and kind '{kind}'.");
        }

        if (obj.Metadata is null)
        {
            throw new K8sValidationException($"{apiVersion}/{kind} is missing metadata.");
        }

        if (string.IsNullOrWhiteSpace(obj.Metadata.Name))
        {
            throw new K8sValidationException($"{apiVersion}/{kind} is missing metadata.name.");
        }

        if (string.IsNullOrWhiteSpace(obj.Metadata.NamespaceProperty))
        {
            obj.Metadata.NamespaceProperty = namespaceName;
        }
        else if (!string.Equals(obj.Metadata.NamespaceProperty, namespaceName, StringComparison.Ordinal))
        {
            throw new K8sValidationException(
                $"{apiVersion}/{kind} '{obj.Metadata.Name}' targets namespace '{obj.Metadata.NamespaceProperty}', but the tool namespace is '{namespaceName}'.");
        }

        return obj;
    }
}

public sealed record K8sParsedManifest(
    IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Objects,
    K8sObjectRef[] ObjectRefs);

public sealed class K8sValidationException : Exception
{
    public K8sValidationException(string message)
        : base(message)
    {
    }
}
