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

    [McpServerTool(Name = K8sConventions.ToolNames.RequestApplyManifest, Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to server-side apply supported Kubernetes YAML or JSON manifests.")]
    public static Task<string> RequestApplyManifest(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON containing Deployments, Services, or ConfigMaps.")] string manifest,
        CancellationToken cancellationToken = default) =>
        manager.RequestApplyManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.RequestDeleteManifest, Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to delete each supported Kubernetes object named in a manifest.")]
    public static Task<string> RequestDeleteManifest(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON naming Deployments, Services, or ConfigMaps to delete.")] string manifest,
        CancellationToken cancellationToken = default) =>
        manager.RequestDeleteManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.RequestScaleDeployment, Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to scale a Deployment in an allowed namespace.")]
    public static Task<string> RequestScaleDeployment(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Number of replicas, from 0 to 5.")] int replicas,
        CancellationToken cancellationToken = default) =>
        manager.RequestScaleDeploymentAsync(@namespace, name, replicas, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.RequestRestartDeployment, Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to restart a Deployment in an allowed namespace.")]
    public static Task<string> RequestRestartDeployment(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        CancellationToken cancellationToken = default) =>
        manager.RequestRestartDeploymentAsync(@namespace, name, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.RequestSetDeploymentImage, Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to update one Deployment container image in an allowed namespace.")]
    public static Task<string> RequestSetDeploymentImage(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Container name.")] string container,
        [Description("Target container image.")] string image,
        CancellationToken cancellationToken = default) =>
        manager.RequestSetDeploymentImageAsync(@namespace, name, container, image, cancellationToken);

    [McpServerTool(Name = K8sConventions.ToolNames.ApplyApprovedPlan, Destructive = true, OpenWorld = false)]
    [Description("Applies a pending Kubernetes plan that was already approved out-of-band.")]
    public static Task<string> ApplyApprovedPlan(
        K8sManager manager,
        [Description("PlanId returned by one of the request_* tools.")] string planId,
        CancellationToken cancellationToken = default) =>
        manager.ApplyApprovedPlanAsync(planId, cancellationToken);
}
