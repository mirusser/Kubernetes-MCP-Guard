using System.ComponentModel;
using ModelContextProtocol.Server;

namespace InfraGate.McpServer;

[McpServerToolType]
public static class K8sTools
{
    [McpServerTool(Name = "get_k8s_status", ReadOnly = true, OpenWorld = false)]
    [Description("Shows a JSON summary of supported Kubernetes resources in an allowed namespace.")]
    public static Task<string> GetK8sStatus(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Optional Kubernetes label selector, for example app=my-app.")] string? labelSelector = null,
        CancellationToken cancellationToken = default) =>
        manager.GetStatusAsync(@namespace, labelSelector, cancellationToken);

    [McpServerTool(Name = "request_apply_manifest", Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to server-side apply supported Kubernetes YAML or JSON manifests.")]
    public static Task<string> RequestApplyManifest(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON containing Deployments, Services, or ConfigMaps.")] string manifest,
        CancellationToken cancellationToken = default) =>
        manager.RequestApplyManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = "request_delete_manifest", Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to delete each supported Kubernetes object named in a manifest.")]
    public static Task<string> RequestDeleteManifest(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON naming Deployments, Services, or ConfigMaps to delete.")] string manifest,
        CancellationToken cancellationToken = default) =>
        manager.RequestDeleteManifestAsync(@namespace, manifest, cancellationToken);

    [McpServerTool(Name = "request_scale_deployment", Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to scale a Deployment in an allowed namespace.")]
    public static Task<string> RequestScaleDeployment(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Number of replicas, from 0 to 5.")] int replicas,
        CancellationToken cancellationToken = default) =>
        manager.RequestScaleDeploymentAsync(@namespace, name, replicas, cancellationToken);

    [McpServerTool(Name = "request_restart_deployment", Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to restart a Deployment in an allowed namespace.")]
    public static Task<string> RequestRestartDeployment(
        K8sManager manager,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        CancellationToken cancellationToken = default) =>
        manager.RequestRestartDeploymentAsync(@namespace, name, cancellationToken);

    [McpServerTool(Name = "apply_approved_plan", Destructive = true, OpenWorld = false)]
    [Description("Applies a previously requested Kubernetes plan only after its exact pending file has been approved out of band.")]
    public static Task<string> ApplyApprovedPlan(
        K8sManager manager,
        [Description("PlanId returned by one of the request_* tools.")] string planId,
        CancellationToken cancellationToken = default) =>
        manager.ApplyApprovedPlanAsync(planId, cancellationToken);
}
