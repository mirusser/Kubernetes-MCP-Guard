using System.ComponentModel;
using ModelContextProtocol.Server;

namespace InfraGate.McpGateway;

[McpServerToolType]
public static class K8sGatewayTools
{
    [McpServerTool(Name = "get_k8s_status", ReadOnly = true, OpenWorld = false)]
    [Description("Shows a JSON summary of supported Kubernetes resources in an allowed namespace.")]
    public static Task<string> GetK8sStatus(
        GuardedToolRunner runner,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Optional Kubernetes label selector, for example app=my-app.")] string? labelSelector = null,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            "get_k8s_status",
            new Dictionary<string, object?>
            {
                ["namespace"] = @namespace,
                ["labelSelector"] = labelSelector
            },
            cancellationToken);

    [McpServerTool(Name = "request_apply_manifest", Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to server-side apply supported Kubernetes YAML or JSON manifests.")]
    public static Task<string> RequestApplyManifest(
        GuardedToolRunner runner,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON containing Deployments, Services, or ConfigMaps.")] string manifest,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            "request_apply_manifest",
            new Dictionary<string, object?>
            {
                ["namespace"] = @namespace,
                ["manifest"] = manifest
            },
            cancellationToken);

    [McpServerTool(Name = "request_delete_manifest", Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to delete each supported Kubernetes object named in a manifest.")]
    public static Task<string> RequestDeleteManifest(
        GuardedToolRunner runner,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON naming Deployments, Services, or ConfigMaps to delete.")] string manifest,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            "request_delete_manifest",
            new Dictionary<string, object?>
            {
                ["namespace"] = @namespace,
                ["manifest"] = manifest
            },
            cancellationToken);

    [McpServerTool(Name = "request_scale_deployment", Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to scale a Deployment in an allowed namespace.")]
    public static Task<string> RequestScaleDeployment(
        GuardedToolRunner runner,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Number of replicas, from 0 to 5.")] int replicas,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            "request_scale_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = @namespace,
                ["name"] = name,
                ["replicas"] = replicas
            },
            cancellationToken);

    [McpServerTool(Name = "request_restart_deployment", Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to restart a Deployment in an allowed namespace.")]
    public static Task<string> RequestRestartDeployment(
        GuardedToolRunner runner,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            "request_restart_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = @namespace,
                ["name"] = name
            },
            cancellationToken);

    [McpServerTool(Name = "apply_approved_plan", Destructive = true, OpenWorld = false)]
    [Description("Requests MCP user approval for a pending Kubernetes plan, then applies the exact approved plan.")]
    public static Task<string> ApplyApprovedPlan(
        GuardedToolRunner runner,
        McpServer server,
        [Description("PlanId returned by one of the request_* tools.")] string planId,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            "apply_approved_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = planId
            },
            server,
            cancellationToken);
}
