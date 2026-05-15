using System.Net;
using InfraGate.Approvals;
using InfraGate.Approvals.AuditPayloads;
using InfraGate.KubernetesAdapter;
using k8s;
using k8s.Autorest;

namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed partial class K8sManager
{
    private const string ServerSideApplyConflictMessage = """
                                                          Apply refused by Kubernetes field ownership conflict.
                                                          The plan was not forced because force apply can take ownership of fields from another manager.
                                                          Re-request the plan after reconciling the live object, or use an explicitly approved force-apply flow if enabled.
                                                          """;

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

    private static IEnumerable<string> DeploymentNames(KubernetesPlan plan)
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

    private Task WriteDryRunFailedAuditAsync(
        string phase,
        KubernetesPlan plan,
        string message,
        CancellationToken cancellationToken) =>
        approvalStore.WriteAuditAsync(
            ApprovalConventions.AuditEvents.DryRunFailed,
            new DryRunFailedPayload(
                phase,
                plan.Id,
                plan.Operation,
                plan.Namespace,
                plan.Objects.Select(FormatObjectRef).ToArray(),
                message),
            cancellationToken);

    private Task WriteDiffFailedAuditAsync(
        KubernetesPlan plan,
        string message,
        CancellationToken cancellationToken) =>
        approvalStore.WriteAuditAsync(
            ApprovalConventions.AuditEvents.DiffFailed,
            new DiffFailedPayload(
                plan.Id,
                plan.Operation,
                plan.Namespace,
                plan.Objects.Select(FormatObjectRef).ToArray(),
                message),
            cancellationToken);

    private static string FormatRequestDryRunRefusal(string message) =>
        $"Server-side dry-run failed; no approval plan was created.{Environment.NewLine}{message}";

    private static string FormatApplyDryRunRefusal(string message) =>
        $"Server-side dry-run failed immediately before apply; refusing to mutate Kubernetes.{Environment.NewLine}{message}";

    private static bool IsNotFound(Exception ex)
    {
        return ex is KubernetesException { Status.Code: 404 } ||
               ex is HttpOperationException { Response.StatusCode: HttpStatusCode.NotFound };
    }

    internal static string FormatServerSideApplyException(string prefix, Exception ex)
    {
        var message = FormatApiException(prefix, ex);

        return IsConflict(ex)
            ? $"{ServerSideApplyConflictMessage}{Environment.NewLine}{message}"
            : message;
    }

    internal static bool IsConflict(Exception ex)
    {
        if (ex is KubernetesException { Status: not null } kube)
        {
            return kube.Status.Code == (int)HttpStatusCode.Conflict ||
                   string.Equals(kube.Status.Reason, "Conflict", StringComparison.OrdinalIgnoreCase);
        }

        return ex is HttpOperationException { Response.StatusCode: HttpStatusCode.Conflict };
    }

    internal static string FormatApiException(string prefix, Exception ex)
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

    internal static bool TryFormatKubernetesException(string prefix, Exception ex, out string message)
    {
        if (ex is KubernetesException { Status: not null } kube)
        {
            message = $"{prefix}: Kubernetes API returned {kube.Status.Code} {kube.Status.Reason}: {kube.Status.Message}";
            return true;
        }

        message = string.Empty;
        return false;
    }

    internal static bool TryFormatHttpOperationException(string prefix, Exception ex, out string message)
    {
        if (ex is HttpOperationException { Response: not null } http)
        {
            message = $"{prefix}: Kubernetes API returned {(int)http.Response.StatusCode} {http.Response.ReasonPhrase}: {http.Message}";
            return true;
        }

        message = string.Empty;
        return false;
    }

    private sealed record ApplyResult(bool Succeeded, string Message)
    {
        public static ApplyResult Success(string message) => new(true, message);

        public static ApplyResult Failed(string message) => new(false, message);
    }
}
