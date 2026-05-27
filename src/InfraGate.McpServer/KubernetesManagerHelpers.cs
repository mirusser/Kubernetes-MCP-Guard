using System.Net;
using System.Text.Json;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.PlanBuilding;
using k8s;
using k8s.Autorest;

namespace InfraGate.McpServer;

internal static class KubernetesManagerHelpers
{
    public const int MaxReplicas = KubernetesConventions.MaxReplicas;
    public const string FieldManager = KubernetesConventions.ServiceName;
    public const string RestartedAtAnnotation = KubernetesConventions.KubernetesResources.RestartedAtAnnotation;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string? ValidateNamespace(KubernetesMcpOptions options, string namespaceName)
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

    public static string? ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? "Resource name is required."
            : null;

    public static string? ValidateRequiredText(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? $"{name} is required."
            : null;

    public static string? ValidateReplicas(int replicas) =>
        replicas is < 0 or > MaxReplicas
            ? $"Replicas must be between 0 and {MaxReplicas}."
            : null;

    public static bool IsNotFound(Exception ex) =>
        ex is KubernetesException { Status.Code: 404 } ||
        ex is HttpOperationException { Response.StatusCode: HttpStatusCode.NotFound };

    public static string FormatServerSideApplyException(string prefix, Exception ex)
    {
        var message = FormatApiException(prefix, ex);
        var conflictMessage =
@"Apply refused by Kubernetes field ownership conflict.
The plan was not forced because force apply can take ownership of fields from another manager.
Re-request the plan after reconciling the live object, or use an explicitly approved force-apply flow if enabled.";

        return IsConflict(ex)
            ? $"{conflictMessage}{Environment.NewLine}{message}"
            : message;
    }

    public static bool IsConflict(Exception ex)
    {
        if (ex is KubernetesException { Status: not null } kube)
        {
            return kube.Status.Code == (int)HttpStatusCode.Conflict ||
                   string.Equals(kube.Status.Reason, "Conflict", StringComparison.OrdinalIgnoreCase);
        }

        return ex is HttpOperationException { Response.StatusCode: HttpStatusCode.Conflict };
    }

    public static string FormatApiException(string prefix, Exception ex)
    {
        if (TryFormatKubernetesException(prefix, ex, out var message))
        {
            return message;
        }

        if (TryFormatHttpOperationException(prefix, ex, out message))
        {
            return message;
        }

        return $"{prefix}: {ex.Message}";
    }

    public static bool TryFormatKubernetesException(string prefix, Exception ex, out string message)
    {
        if (ex is KubernetesException { Status: not null } kube)
        {
            message = $"{prefix}: Kubernetes API returned {kube.Status.Code} {kube.Status.Reason}: {kube.Status.Message}";
            return true;
        }

        message = string.Empty;
        return false;
    }

    public static bool TryFormatHttpOperationException(string prefix, Exception ex, out string message)
    {
        if (ex is HttpOperationException { Response: not null } http)
        {
            message = $"{prefix}: Kubernetes API returned {(int)http.Response.StatusCode} {http.Response.ReasonPhrase}: {http.Message}";
            return true;
        }

        message = string.Empty;
        return false;
    }

    public static string FormatObjectRef(KubernetesObjectRef obj) =>
        $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";
}
