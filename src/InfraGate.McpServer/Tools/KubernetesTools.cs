using System.ComponentModel;
using ModelContextProtocol.Server;

namespace InfraGate.McpServer;

[McpServerToolType]
public static class KubernetesTools
{
    [McpServerTool(Name = KubernetesConventions.ToolNames.GetAllowedNamespaces, ReadOnly = true, OpenWorld = false)]
    [Description("Returns the list of Kubernetes namespaces this server is allowed to access.")]
    public static Task<string> GetAllowedNamespaces(KubernetesManager manager) =>
        manager.GetAllowedNamespacesAsync();

    [McpServerTool(Name = KubernetesConventions.ToolNames.GetK8sStatus, ReadOnly = true, OpenWorld = false)]
    [Description("Shows a JSON summary of supported Kubernetes resources in an allowed namespace.")]
    public static Task<string> GetK8sStatus(
        KubernetesManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Optional Kubernetes label selector, for example app=my-app.")] string? labelSelector = null,
        CancellationToken cancellationToken = default) =>
        manager.GetStatusAsync(@namespace, labelSelector, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.GetK8sEvents, ReadOnly = true, OpenWorld = false)]
    [Description("Shows a bounded JSON summary of Kubernetes events in an allowed namespace.")]
    public static Task<string> GetK8sEvents(
        KubernetesManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Optional Kubernetes label selector, for example app=my-app.")] string? labelSelector = null,
        [Description("Optional Kubernetes field selector, for example regarding.name=my-pod.")] string? fieldSelector = null,
        [Description("Maximum events to return, from 1 to 100.")] int limit = KubernetesConventions.DefaultEventLimit,
        [Description("Optional event types to exclude, for example [\"Normal\"].")] string[]? excludeEventTypes = null,
        CancellationToken cancellationToken = default) =>
        manager.GetEventsAsync(@namespace, labelSelector, fieldSelector, limit, excludeEventTypes, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.GetPodLogs, ReadOnly = true, OpenWorld = false)]
    [Description("Shows bounded logs for a Pod in an allowed namespace.")]
    public static Task<string> GetPodLogs(
        KubernetesManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Pod name.")] string podName,
        [Description("Optional container name.")] string? container = null,
        [Description("Number of log lines from the end, from 1 to 500.")] int tailLines = KubernetesConventions.DefaultLogTailLines,
        [Description("Whether to read previous terminated container logs.")] bool previous = false,
        CancellationToken cancellationToken = default) =>
        manager.GetPodLogsAsync(@namespace, podName, container, tailLines, previous, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.GetK8sResource, ReadOnly = true, OpenWorld = false)]
    [Description("Shows a focused JSON summary of one supported Kubernetes resource in an allowed namespace.")]
    public static Task<string> GetK8sResource(
        KubernetesManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Resource kind: Deployment, ReplicaSet, Pod, Service, or ConfigMap.")] string kind,
        [Description("Resource name.")] string name,
        CancellationToken cancellationToken = default) =>
        manager.GetResourceAsync(@namespace, kind, name, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.GetDeploymentDiagnostics, ReadOnly = true, OpenWorld = false)]
    [Description("Shows bounded Deployment diagnostics with related ReplicaSets, Pods, and Events in an allowed namespace.")]
    public static Task<string> GetDeploymentDiagnostics(
        KubernetesManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Maximum related events to return, from 1 to 100.")] int limit = KubernetesConventions.DefaultEventLimit,
        CancellationToken cancellationToken = default) =>
        manager.GetDeploymentDiagnosticsAsync(@namespace, name, limit, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.GetPodDiagnostics, ReadOnly = true, OpenWorld = false)]
    [Description("Shows bounded Pod diagnostics with related Events in an allowed namespace.")]
    public static Task<string> GetPodDiagnostics(
        KubernetesManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Pod name.")] string podName,
        [Description("Maximum related events to return, from 1 to 100.")] int limit = KubernetesConventions.DefaultEventLimit,
        CancellationToken cancellationToken = default) =>
        manager.GetPodDiagnosticsAsync(@namespace, podName, limit, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.GetServiceDiagnostics, ReadOnly = true, OpenWorld = false)]
    [Description("Shows bounded Service diagnostics with matching Pods and related Events in an allowed namespace.")]
    public static Task<string> GetServiceDiagnostics(
        KubernetesManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Service name.")] string name,
        [Description("Maximum related events to return, from 1 to 100.")] int limit = KubernetesConventions.DefaultEventLimit,
        CancellationToken cancellationToken = default) =>
        manager.GetServiceDiagnosticsAsync(@namespace, name, limit, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.DryRunApplyManifest, ReadOnly = true, OpenWorld = false)]
    [Description("Server-side dry-run of applying supported Kubernetes YAML or JSON manifests. Returns JSON-serialized dry-run result.")]
    public static Task<string> DryRunApplyManifest(
        KubernetesEvidenceService evidence,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON containing Deployments, Services, or ConfigMaps.")] string manifest,
        CancellationToken cancellationToken = default) =>
        evidence.EvidenceDryRunApplyManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.DryRunDeleteManifest, ReadOnly = true, OpenWorld = false)]
    [Description("Server-side dry-run of deleting Kubernetes objects named in a manifest. Returns JSON-serialized dry-run result.")]
    public static Task<string> DryRunDeleteManifest(
        KubernetesEvidenceService evidence,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON naming Deployments, Services, or ConfigMaps to delete.")] string manifest,
        CancellationToken cancellationToken = default) =>
        evidence.EvidenceDryRunDeleteManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.DryRunScaleDeployment, ReadOnly = true, OpenWorld = false)]
    [Description("Server-side dry-run of scaling a Deployment. Returns JSON-serialized dry-run result.")]
    public static Task<string> DryRunScaleDeployment(
        KubernetesEvidenceService evidence,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Number of replicas, from 0 to 5.")] int replicas,
        CancellationToken cancellationToken = default) =>
        evidence.EvidenceDryRunScaleDeploymentAsync(@namespace, name, replicas, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.DryRunRestartDeployment, ReadOnly = true, OpenWorld = false)]
    [Description("Server-side dry-run of restarting a Deployment. Returns JSON-serialized dry-run result.")]
    public static Task<string> DryRunRestartDeployment(
        KubernetesEvidenceService evidence,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        CancellationToken cancellationToken = default) =>
        evidence.EvidenceDryRunRestartDeploymentAsync(@namespace, name, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.DryRunSetDeploymentImage, ReadOnly = true, OpenWorld = false)]
    [Description("Server-side dry-run of updating a Deployment container image. Returns JSON-serialized dry-run result.")]
    public static Task<string> DryRunSetDeploymentImage(
        KubernetesEvidenceService evidence,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Container name.")] string container,
        [Description("Target container image.")] string image,
        CancellationToken cancellationToken = default) =>
        evidence.EvidenceDryRunSetDeploymentImageAsync(@namespace, name, container, image, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.DiffManifest, ReadOnly = true, OpenWorld = false)]
    [Description("Computes a diff between live Kubernetes state and the proposed application of a manifest. Returns JSON-serialized diff result.")]
    public static Task<string> DiffManifest(
        KubernetesEvidenceService evidence,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON containing Deployments, Services, or ConfigMaps.")] string manifest,
        CancellationToken cancellationToken = default) =>
        evidence.EvidenceDiffManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.CheckLiveDrift, ReadOnly = true, OpenWorld = false)]
    [Description("Checks whether live Kubernetes state has drifted from the recorded plan diffs. Returns 'ok' if no drift, or a description of detected drift.")]
    public static Task<string> CheckLiveDrift(
        KubernetesEvidenceService evidence,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("The mutation operation type (e.g. apply, delete, scale).")] string operation,
        [Description("JSON-serialized array of KubernetesPlanDiff recorded at plan creation time.")] string diffsJson,
        CancellationToken cancellationToken = default) =>
        evidence.EvidenceCheckLiveDriftAsync(@namespace, operation, diffsJson, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.CheckResourceVersion, ReadOnly = true, OpenWorld = false)]
    [Description("Checks whether live Kubernetes object resourceVersions match the expected values captured at plan creation time. Returns 'ok' if all match, or a description of detected mismatch.")]
    public static Task<string> CheckResourceVersion(
        KubernetesEvidenceService evidence,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("JSON-serialized dictionary of { object-key: resourceVersion } pairs.")] string diffsJson,
        CancellationToken cancellationToken = default) =>
        evidence.EvidenceCheckResourceVersionAsync(@namespace, diffsJson, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.DiffDeployment, ReadOnly = true, OpenWorld = false)]
    [Description("Computes a diff between live Kubernetes state and the proposed mutation of a Deployment. Returns JSON-serialized diff result.")]
    public static Task<string> DiffDeployment( // NOSONAR:S107 — MCP tool signature dictated by framework convention.
        KubernetesEvidenceService evidence,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("The mutation operation type (scale, restart, set-image).")] string operation,
        [Description("Replicas count (required for scale).")] int? replicas = null,
        [Description("Container name (required for set-image).")] string? container = null,
        [Description("Image reference (required for set-image).")] string? image = null,
        CancellationToken cancellationToken = default) =>
        evidence.EvidenceDiffDeploymentAsync(@namespace, name, operation, replicas, container, image, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.ApplyManifest, Destructive = true, OpenWorld = false)]
    [Description("Applies supported Kubernetes YAML or JSON manifests directly (no approval flow).")]
    public static Task<string> ApplyManifest(
        KubernetesExecutionService execution,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON containing Deployments, Services, or ConfigMaps.")] string manifest,
        [Description("Optional JSON array of {Key, ResourceVersion} pairs for optimistic concurrency.")] string? resourceVersions = null,
        CancellationToken cancellationToken = default) =>
        execution.ExecuteApplyManifestAsync(@namespace, manifest, resourceVersions, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.DeleteManifest, Destructive = true, OpenWorld = false)]
    [Description("Deletes each supported Kubernetes object named in a manifest directly (no approval flow).")]
    public static Task<string> DeleteManifest(
        KubernetesExecutionService execution,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON identifying objects to delete.")] string manifest,
        CancellationToken cancellationToken = default) =>
        execution.ExecuteDeleteManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.ScaleDeployment, Destructive = true, OpenWorld = false)]
    [Description("Scales a Deployment to the specified replica count directly (no approval flow).")]
    public static Task<string> ScaleDeployment(
        KubernetesExecutionService execution,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Number of replicas (0–5).")] int replicas,
        CancellationToken cancellationToken = default) =>
        execution.ExecuteScaleDeploymentAsync(@namespace, name, replicas, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.RestartDeployment, Destructive = true, OpenWorld = false)]
    [Description("Performs a rolling restart of a Deployment directly (no approval flow).")]
    public static Task<string> RestartDeployment(
        KubernetesExecutionService execution,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        CancellationToken cancellationToken = default) =>
        execution.ExecuteRestartDeploymentAsync(@namespace, name, cancellationToken);

    [McpServerTool(Name = KubernetesConventions.ToolNames.SetDeploymentImage, Destructive = true, OpenWorld = false)]
    [Description("Updates a container image in a Deployment directly (no approval flow).")]
    public static Task<string> SetDeploymentImage(
        KubernetesExecutionService execution,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Container name within the Deployment.")] string container,
        [Description("New container image reference.")] string image,
        CancellationToken cancellationToken = default) =>
        execution.ExecuteSetDeploymentImageAsync(@namespace, name, container, image, cancellationToken);
}
