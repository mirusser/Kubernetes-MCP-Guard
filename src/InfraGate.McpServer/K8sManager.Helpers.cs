using System.Net;
using k8s;
using k8s.Autorest;

namespace InfraGate.McpServer;

public sealed partial class K8sManager
{
    private string? ValidateNamespace(string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return "Namespace is required.";
        }

        if (!options.IsNamespaceAllowed(namespaceName))
        {
            return $"Namespace '{namespaceName}' is not allowed. Allowed namespaces: {string.Join(", ", options.AllowedNamespaces.Order(StringComparer.Ordinal))}.";
        }

        return null;
    }

    private static string? ValidateName(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? "Resource name is required."
            : null;
    }

    private static string? ValidateRequiredText(string value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? $"{name} is required."
            : null;
    }

    private static string? ValidateReplicas(int replicas)
    {
        return replicas is < 0 or > MaxReplicas
            ? $"Replicas must be between 0 and {MaxReplicas}."
            : null;
    }

    private static IEnumerable<string> DeploymentNames(K8sPlan plan)
    {
        return plan.Objects
            .Where(K8sConventions.K8sResources.IsDeployment)
            .Select(obj => obj.Name);
    }

    private static bool SameObjects(K8sObjectRef[] left, K8sObjectRef[] right)
    {
        static string Key(K8sObjectRef obj) => $"{obj.ApiVersion}/{obj.Kind}/{obj.Namespace}/{obj.Name}";

        return left.Select(Key).Order(StringComparer.Ordinal)
            .SequenceEqual(right.Select(Key).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static bool IsNotFound(Exception ex)
    {
        return ex is KubernetesException { Status.Code: 404 } ||
               ex is HttpOperationException { Response.StatusCode: HttpStatusCode.NotFound };
    }

    private static string FormatApiException(string prefix, Exception ex)
    {
        return ex switch
        {
            KubernetesException kube when kube.Status is not null =>
                $"{prefix}: Kubernetes API returned {kube.Status.Code} {kube.Status.Reason}: {kube.Status.Message}",
            HttpOperationException http when http.Response is not null =>
                $"{prefix}: Kubernetes API returned {(int)http.Response.StatusCode} {http.Response.ReasonPhrase}: {http.Message}",
            _ => $"{prefix}: {ex.Message}"
        };
    }

    private sealed record ApplyResult(bool Succeeded, string Message)
    {
        public static ApplyResult Success(string message) => new(true, message);

        public static ApplyResult Failed(string message) => new(false, message);
    }
}
