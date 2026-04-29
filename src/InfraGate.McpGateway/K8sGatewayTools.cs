using System.ComponentModel;
using ModelContextProtocol.Server;

namespace InfraGate.McpGateway;

[McpServerToolType]
public static class K8sGatewayTools
{
    [McpServerTool(Name = McpGatewayConventions.ToolNames.GetK8sStatus, ReadOnly = true, OpenWorld = false)]
    [Description("Shows a JSON summary of supported Kubernetes resources in an allowed namespace.")]
    public static Task<string> GetK8sStatus(
        GuardedToolRunner runner,
        [Description("Allowed Kubernetes namespace to inspect.")] string @namespace,
        [Description("Optional Kubernetes label selector, for example app=my-app.")] string? labelSelector = null,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            McpGatewayConventions.ToolNames.GetK8sStatus,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.Namespace] = @namespace,
                [McpGatewayConventions.ToolArguments.LabelSelector] = labelSelector
            },
            cancellationToken);

    [McpServerTool(Name = McpGatewayConventions.ToolNames.RequestApplyManifest, Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to server-side apply supported Kubernetes YAML or JSON manifests.")]
    public static Task<string> RequestApplyManifest(
        GuardedToolRunner runner,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON containing Deployments, Services, or ConfigMaps.")] string manifest,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            McpGatewayConventions.ToolNames.RequestApplyManifest,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.Namespace] = @namespace,
                [McpGatewayConventions.ToolArguments.Manifest] = manifest
            },
            cancellationToken);

    [McpServerTool(Name = McpGatewayConventions.ToolNames.RequestDeleteManifest, Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to delete each supported Kubernetes object named in a manifest.")]
    public static Task<string> RequestDeleteManifest(
        GuardedToolRunner runner,
        [Description("Allowed Kubernetes namespace for the manifest.")] string @namespace,
        [Description("Multi-document YAML or JSON naming Deployments, Services, or ConfigMaps to delete.")] string manifest,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            McpGatewayConventions.ToolNames.RequestDeleteManifest,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.Namespace] = @namespace,
                [McpGatewayConventions.ToolArguments.Manifest] = manifest
            },
            cancellationToken);

    [McpServerTool(Name = McpGatewayConventions.ToolNames.RequestScaleDeployment, Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to scale a Deployment in an allowed namespace.")]
    public static Task<string> RequestScaleDeployment(
        GuardedToolRunner runner,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        [Description("Number of replicas, from 0 to 5.")] int replicas,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            McpGatewayConventions.ToolNames.RequestScaleDeployment,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.Namespace] = @namespace,
                [McpGatewayConventions.ToolArguments.Name] = name,
                [McpGatewayConventions.ToolArguments.Replicas] = replicas
            },
            cancellationToken);

    [McpServerTool(Name = McpGatewayConventions.ToolNames.RequestRestartDeployment, Destructive = false, OpenWorld = false)]
    [Description("Creates a pending approval plan to restart a Deployment in an allowed namespace.")]
    public static Task<string> RequestRestartDeployment(
        GuardedToolRunner runner,
        [Description("Allowed Kubernetes namespace.")] string @namespace,
        [Description("Deployment name.")] string name,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            McpGatewayConventions.ToolNames.RequestRestartDeployment,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.Namespace] = @namespace,
                [McpGatewayConventions.ToolArguments.Name] = name
            },
            cancellationToken);

    [McpServerTool(Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan, Destructive = true, OpenWorld = false)]
    [Description("Requests MCP user approval for a pending Kubernetes plan, then applies the exact approved plan.")]
    public static Task<string> ApplyApprovedPlan(
        GuardedToolRunner runner,
        McpServer server,
        [Description("PlanId returned by one of the request_* tools.")] string planId,
        CancellationToken cancellationToken = default) =>
        runner.CallAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            },
            server,
            cancellationToken);
}
