using System.ComponentModel;
using ModelContextProtocol.Server;

namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
[McpServerToolType]
public static class K8sTools
{
    [McpServerTool(Name = K8sConventions.ToolNames.GetAllowedNamespaces, ReadOnly = true, OpenWorld = false)]
    [Description("Returns the list of Kubernetes namespaces this server is allowed to access.")]
    public static Task<string> GetAllowedNamespaces(K8sManager manager) =>
        manager.GetAllowedNamespacesAsync();

    [McpServerTool(Name = K8sConventions.ToolNames.GetK8sStatus, ReadOnly = true, OpenWorld = false)]
    [Description("Shows a JSON summary of supported Kubernetes resources in an allowed namespace.")]
    public static Task<string> GetK8sStatus(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Optional Kubernetes label selector, for example app=my-app.")] string? labelSelector = null,
        CancellationToken cancellationToken = default) =>
        manager.GetStatusAsync(@namespace, labelSelector, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.GetK8sEvents, ReadOnly = true, OpenWorld = false)]
    [Description("Shows a bounded JSON summary of Kubernetes events in an allowed namespace.")]
    public static Task<string> GetK8sEvents(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Optional Kubernetes label selector, for example app=my-app.")] string? labelSelector = null,
        [Description("Optional Kubernetes field selector, for example regarding.name=my-pod.")] string? fieldSelector = null,
        [Description("Maximum events to return, from 1 to 100.")] int limit = K8sConventions.DefaultEventLimit,
        CancellationToken cancellationToken = default) =>
        manager.GetEventsAsync(@namespace, labelSelector, fieldSelector, limit, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.GetPodLogs, ReadOnly = true, OpenWorld = false)]
    [Description("Shows bounded logs for a Pod in an allowed namespace.")]
    public static Task<string> GetPodLogs(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Pod name.")] string podName,
        [Description("Optional container name.")] string? container = null,
        [Description("Number of log lines from the end, from 1 to 500.")] int tailLines = K8sConventions.DefaultLogTailLines,
        [Description("Whether to read previous terminated container logs.")] bool previous = false,
        CancellationToken cancellationToken = default) =>
        manager.GetPodLogsAsync(@namespace, podName, container, tailLines, previous, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.GetK8sResource, ReadOnly = true, OpenWorld = false)]
    [Description("Shows a focused JSON summary of one supported Kubernetes resource in an allowed namespace.")]
    public static Task<string> GetK8sResource(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Resource kind: Deployment, ReplicaSet, Pod, Service, or ConfigMap.")] string kind,
        [Description("Resource name.")] string name,
        CancellationToken cancellationToken = default) =>
        manager.GetResourceAsync(@namespace, kind, name, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.GetDeploymentDiagnostics, ReadOnly = true, OpenWorld = false)]
    [Description("Shows bounded Deployment diagnostics with related ReplicaSets, Pods, and Events in an allowed namespace.")]
    public static Task<string> GetDeploymentDiagnostics(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Maximum related events to return, from 1 to 100.")] int limit = K8sConventions.DefaultEventLimit,
        CancellationToken cancellationToken = default) =>
        manager.GetDeploymentDiagnosticsAsync(@namespace, name, limit, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.GetPodDiagnostics, ReadOnly = true, OpenWorld = false)]
    [Description("Shows bounded Pod diagnostics with related Events in an allowed namespace.")]
    public static Task<string> GetPodDiagnostics(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Pod name.")] string podName,
        [Description("Maximum related events to return, from 1 to 100.")] int limit = K8sConventions.DefaultEventLimit,
        CancellationToken cancellationToken = default) =>
        manager.GetPodDiagnosticsAsync(@namespace, podName, limit, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.GetServiceDiagnostics, ReadOnly = true, OpenWorld = false)]
    [Description("Shows bounded Service diagnostics with matching Pods and related Events in an allowed namespace.")]
    public static Task<string> GetServiceDiagnostics(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Service name.")] string name,
        [Description("Maximum related events to return, from 1 to 100.")] int limit = K8sConventions.DefaultEventLimit,
        CancellationToken cancellationToken = default) =>
        manager.GetServiceDiagnosticsAsync(@namespace, name, limit, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.DryRunApplyManifest, ReadOnly = true, OpenWorld = false)]
    [Description("Server-side dry-run of applying supported Kubernetes YAML or JSON manifests. Returns JSON-serialized dry-run result.")]
    public static Task<string> DryRunApplyManifest(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON containing Deployments, Services, or ConfigMaps.")] string manifest,
        CancellationToken cancellationToken = default) =>
        manager.EvidenceDryRunApplyManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.DryRunDeleteManifest, ReadOnly = true, OpenWorld = false)]
    [Description("Server-side dry-run of deleting Kubernetes objects named in a manifest. Returns JSON-serialized dry-run result.")]
    public static Task<string> DryRunDeleteManifest(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON naming Deployments, Services, or ConfigMaps to delete.")] string manifest,
        CancellationToken cancellationToken = default) =>
        manager.EvidenceDryRunDeleteManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.DryRunScaleDeployment, ReadOnly = true, OpenWorld = false)]
    [Description("Server-side dry-run of scaling a Deployment. Returns JSON-serialized dry-run result.")]
    public static Task<string> DryRunScaleDeployment(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Number of replicas, from 0 to 5.")] int replicas,
        CancellationToken cancellationToken = default) =>
        manager.EvidenceDryRunScaleDeploymentAsync(@namespace, name, replicas, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.DryRunRestartDeployment, ReadOnly = true, OpenWorld = false)]
    [Description("Server-side dry-run of restarting a Deployment. Returns JSON-serialized dry-run result.")]
    public static Task<string> DryRunRestartDeployment(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        CancellationToken cancellationToken = default) =>
        manager.EvidenceDryRunRestartDeploymentAsync(@namespace, name, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.DryRunSetDeploymentImage, ReadOnly = true, OpenWorld = false)]
    [Description("Server-side dry-run of updating a Deployment container image. Returns JSON-serialized dry-run result.")]
    public static Task<string> DryRunSetDeploymentImage(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Container name.")] string container,
        [Description("Target container image.")] string image,
        CancellationToken cancellationToken = default) =>
        manager.EvidenceDryRunSetDeploymentImageAsync(@namespace, name, container, image, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.DiffManifest, ReadOnly = true, OpenWorld = false)]
    [Description("Computes a diff between live Kubernetes state and the proposed application of a manifest. Returns JSON-serialized diff result.")]
    public static Task<string> DiffManifest(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON containing Deployments, Services, or ConfigMaps.")] string manifest,
        CancellationToken cancellationToken = default) =>
        manager.EvidenceDiffManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.CheckLiveDrift, ReadOnly = true, OpenWorld = false)]
    [Description("Checks whether live Kubernetes state has drifted from the recorded plan diffs. Returns 'ok' if no drift, or a description of detected drift.")]
    public static Task<string> CheckLiveDrift(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("The mutation operation type (e.g. apply, delete, scale).")] string operation,
        [Description("JSON-serialized array of K8sPlanDiff recorded at plan creation time.")] string diffsJson,
        CancellationToken cancellationToken = default) =>
        manager.EvidenceCheckLiveDriftAsync(@namespace, operation, diffsJson, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.DiffDeployment, ReadOnly = true, OpenWorld = false)]
    [Description("Computes a diff between live Kubernetes state and the proposed mutation of a Deployment. Returns JSON-serialized diff result.")]
    public static Task<string> DiffDeployment(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("The mutation operation type (scale, restart, set-image).")] string operation,
        [Description("Replicas count (required for scale).")] int? replicas = null,
        [Description("Container name (required for set-image).")] string? container = null,
        [Description("Image reference (required for set-image).")] string? image = null,
        CancellationToken cancellationToken = default) =>
        manager.EvidenceDiffDeploymentAsync(@namespace, name, operation, replicas, container, image, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.ApplyManifest, Destructive = true, OpenWorld = false)]
    [Description("Applies supported Kubernetes YAML or JSON manifests directly (no approval flow).")]
    public static Task<string> ApplyManifest(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON containing Deployments, Services, or ConfigMaps.")] string manifest,
        CancellationToken cancellationToken = default) =>
        manager.ExecuteApplyManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.DeleteManifest, Destructive = true, OpenWorld = false)]
    [Description("Deletes each supported Kubernetes object named in a manifest directly (no approval flow).")]
    public static Task<string> DeleteManifest(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON identifying objects to delete.")] string manifest,
        CancellationToken cancellationToken = default) =>
        manager.ExecuteDeleteManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.ScaleDeployment, Destructive = true, OpenWorld = false)]
    [Description("Scales a Deployment to the specified replica count directly (no approval flow).")]
    public static Task<string> ScaleDeployment(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Number of replicas (0–5).")] int replicas,
        CancellationToken cancellationToken = default) =>
        manager.ExecuteScaleDeploymentAsync(@namespace, name, replicas, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.RestartDeployment, Destructive = true, OpenWorld = false)]
    [Description("Performs a rolling restart of a Deployment directly (no approval flow).")]
    public static Task<string> RestartDeployment(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        CancellationToken cancellationToken = default) =>
        manager.ExecuteRestartDeploymentAsync(@namespace, name, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.SetDeploymentImage, Destructive = true, OpenWorld = false)]
    [Description("Updates a container image in a Deployment directly (no approval flow).")]
    public static Task<string> SetDeploymentImage(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Container name within the Deployment.")] string container,
        [Description("New container image reference.")] string image,
        CancellationToken cancellationToken = default) =>
        manager.ExecuteSetDeploymentImageAsync(@namespace, name, container, image, cancellationToken);
}
