using System.ComponentModel;
using k8s;
using k8s.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpServer;

public sealed partial class K8sManager
{
    public Task<string> ApplyApprovedPlanAsync(string planId, CancellationToken cancellationToken) =>
        ApplyApprovedPlanAsync(planId, server: null, cancellationToken);

    public async Task<string> ApplyApprovedPlanAsync(
        string planId,
        ModelContextProtocol.Server.McpServer? server,
        CancellationToken cancellationToken)
    {
        var approved = await approvalStore.GetApprovedPlanAsync(planId, cancellationToken);
        if (!approved.IsApproved && server is not null)
        {
            approved = await RequestServerApprovalAsync(planId, server, cancellationToken);
        }

        if (!approved.IsApproved || approved.Plan is null || approved.Hash is null)
        {
            await approvalStore.WriteAuditAsync(K8sConventions.AuditEvents.ApplyDenied, new
            {
                planId,
                approved.Message
            }, cancellationToken);

            return $"Refused: {approved.Message}";
        }

        var applyResult = await ApplyPlanAsync(approved.Plan, cancellationToken);
        if (!applyResult.Succeeded)
        {
            await approvalStore.WriteAuditAsync(K8sConventions.AuditEvents.ApplyFailed, new
            {
                approved.Plan.Id,
                approved.Plan.Operation,
                applyResult.Message
            }, cancellationToken);

            return applyResult.Message;
        }

        await approvalStore.MarkAppliedAsync(approved.Plan, approved.Hash, cancellationToken);

        var rollout = approved.Plan.Operation == K8sConventions.PlanOperations.Delete
            ? "No rollout wait for delete operations."
            : await WaitForDeploymentsAsync(approved.Plan.Namespace, DeploymentNames(approved.Plan), cancellationToken);
        var status = await GetStatusAsync(approved.Plan.Namespace, labelSelector: null, cancellationToken);

        return $"""
               Applied plan: {approved.Plan.Id}
               Operation: {approved.Plan.Operation}

               API operations:
               {applyResult.Message}

               Rollout:
               {rollout}

               Current status:
               {status}
               """;
    }

    private async Task<ApprovedPlanResult> RequestServerApprovalAsync(
        string planId,
        ModelContextProtocol.Server.McpServer server,
        CancellationToken cancellationToken)
    {
        var pending = await approvalStore.GetPendingPlanAsync(planId, cancellationToken);
        if (TryDenyInvalidPendingPlan(pending, out var denied))
        {
            return denied;
        }

        var plan = pending.Plan!;
        var hash = pending.Hash!;
        var message = FormatApprovalRequest(plan, hash);
        var approvalResult = await ElicitPlanApprovalAsync(planId, message, server, cancellationToken);
        if (approvalResult.DenialMessage is not null)
        {
            return ApprovedPlanResult.Denied(approvalResult.DenialMessage);
        }

        return await ApproveMatchingPlanAsync(planId, hash, approvalResult.Approval!, cancellationToken);
    }

    private static bool TryDenyInvalidPendingPlan(PendingPlanResult pending, out ApprovedPlanResult denied)
    {
        if (IsValidPendingPlan(pending))
        {
            denied = ApprovedPlanResult.Denied(string.Empty);
            return false;
        }

        denied = ApprovedPlanResult.Denied(pending.Message);
        return true;
    }

    private static bool IsValidPendingPlan(PendingPlanResult pending) =>
        pending.IsPending &&
        pending.Plan is not null &&
        pending.Hash is not null;

    private async Task<ApprovedPlanResult> ApproveMatchingPlanAsync(
        string planId,
        string hash,
        PlanApprovalInput approval,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(approval.PlanId, planId, StringComparison.Ordinal))
        {
            return ApprovedPlanResult.Denied($"Plan '{planId}' approval did not echo the matching plan id.");
        }

        return await approvalStore.ApprovePendingPlanAsync(planId, hash, cancellationToken);
    }

    private static async Task<(PlanApprovalInput? Approval, string? DenialMessage)> ElicitPlanApprovalAsync(
        string planId,
        string message,
        ModelContextProtocol.Server.McpServer server,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadPlanApprovalAsync(planId, message, server, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or McpException)
        {
            return (null,
                $"Plan '{planId}' requires MCP server approval, but the client could not complete elicitation: {ex.Message}");
        }
    }

    private static async Task<(PlanApprovalInput? Approval, string? DenialMessage)> ReadPlanApprovalAsync(
        string planId,
        string message,
        ModelContextProtocol.Server.McpServer server,
        CancellationToken cancellationToken)
    {
        var result = await server.ElicitAsync<PlanApprovalInput>(message, cancellationToken: cancellationToken);
        if (!result.IsAccepted)
        {
            return DeniedPlanApproval(planId);
        }

        return AcceptedPlanApproval(planId, result.Content);
    }

    private static (PlanApprovalInput? Approval, string? DenialMessage) AcceptedPlanApproval(
        string planId,
        PlanApprovalInput? approval)
    {
        if (approval is { Approve: true })
        {
            return (approval, null);
        }

        return DeniedPlanApproval(planId);
    }

    private static (PlanApprovalInput? Approval, string? DenialMessage) DeniedPlanApproval(string planId) =>
        (null, $"Plan '{planId}' was not approved through MCP elicitation.");

    private static string FormatApprovalRequest(K8sPlan plan, string hash)
    {
        var objects = string.Join(
            Environment.NewLine,
            plan.Objects.Select(obj => $"- {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}"));

        return $"""
               Approve this Kubernetes plan before applying it.

               PlanId: {plan.Id}
               Operation: {plan.Operation}
               Namespace: {plan.Namespace}
               Description: {plan.Description}
               Objects:
               {objects}
               Plan hash: {hash}
               """;
    }

    private async Task<ApplyResult> ApplyPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            return plan.Operation switch
            {
                K8sConventions.PlanOperations.Apply => await ApplyManifestPlanAsync(plan, cancellationToken),
                K8sConventions.PlanOperations.Delete => await DeleteManifestPlanAsync(plan, cancellationToken),
                K8sConventions.PlanOperations.Scale => await ScaleDeploymentPlanAsync(plan, cancellationToken),
                K8sConventions.PlanOperations.Restart => await RestartDeploymentPlanAsync(plan, cancellationToken),
                K8sConventions.PlanOperations.SetImage => await SetDeploymentImagePlanAsync(plan, cancellationToken),
                _ => ApplyResult.Failed($"Unsupported plan operation '{plan.Operation}'.")
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApplyResult.Failed(FormatApiException("API operation failed", ex));
        }
    }

    private async Task<ApplyResult> ApplyManifestPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plan.Manifest))
        {
            return ApplyResult.Failed("Apply plan is missing a manifest.");
        }

        var parsed = K8sManifestParser.ParseSupported(plan.Manifest, plan.Namespace);
        if (!SameObjects(parsed.ObjectRefs, plan.Objects))
        {
            return ApplyResult.Failed("Apply plan manifest no longer matches the planned object references.");
        }

        var messages = new List<string>();
        foreach (var obj in parsed.Objects)
        {
            await ApplyObjectAsync(obj, cancellationToken);
            messages.Add($"Applied {obj.ApiVersion} {obj.Kind} {obj.Metadata.NamespaceProperty}/{obj.Metadata.Name}");
        }

        return ApplyResult.Success(string.Join(Environment.NewLine, messages));
    }

    private async Task<ApplyResult> DeleteManifestPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        foreach (var obj in plan.Objects)
        {
            messages.Add(await DeleteObjectAsync(obj, cancellationToken));
        }

        return ApplyResult.Success(string.Join(Environment.NewLine, messages));
    }

    private async Task<ApplyResult> ScaleDeploymentPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        var name = plan.Parameters[K8sConventions.PlanParameters.Name];
        var replicas = int.Parse(plan.Parameters[K8sConventions.PlanParameters.Replicas]);
        var patch = new
        {
            spec = new
            {
                replicas
            }
        };

        await client.AppsV1.PatchNamespacedDeploymentScaleAsync(
            new V1Patch(patch, V1Patch.PatchType.MergePatch),
            name,
            plan.Namespace,
            fieldManager: FieldManager,
            cancellationToken: cancellationToken);

        return ApplyResult.Success(
            $"Scaled {K8sConventions.K8sResources.DeploymentDisplayName} {plan.Namespace}/{name} to {replicas} replicas.");
    }

    private async Task<ApplyResult> RestartDeploymentPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        var name = plan.Parameters[K8sConventions.PlanParameters.Name];
        var restartedAtUtc = plan.Parameters[K8sConventions.PlanParameters.RestartedAtUtc];
        var patch = new
        {
            spec = new
            {
                template = new
                {
                    metadata = new
                    {
                        annotations = new Dictionary<string, string>
                        {
                            [RestartedAtAnnotation] = restartedAtUtc
                        }
                    }
                }
            }
        };

        await client.AppsV1.PatchNamespacedDeploymentAsync(
            new V1Patch(patch, V1Patch.PatchType.StrategicMergePatch),
            name,
            plan.Namespace,
            fieldManager: FieldManager,
            cancellationToken: cancellationToken);

        return ApplyResult.Success(
            $"Restarted {K8sConventions.K8sResources.DeploymentDisplayName} {plan.Namespace}/{name} at {restartedAtUtc}.");
    }

    private async Task<ApplyResult> SetDeploymentImagePlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        var name = plan.Parameters[K8sConventions.PlanParameters.Name];
        var container = plan.Parameters[K8sConventions.PlanParameters.Container];
        var currentImage = plan.Parameters[K8sConventions.PlanParameters.CurrentImage];
        var image = plan.Parameters[K8sConventions.PlanParameters.Image];
        var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(
            name,
            plan.Namespace,
            cancellationToken: cancellationToken);
        var deploymentContainer = FindDeploymentContainer(deployment, container);
        var imageValidation = ValidatePlannedContainerImage(
            plan.Namespace,
            name,
            container,
            currentImage,
            deploymentContainer);
        if (imageValidation is not null)
        {
            return ApplyResult.Failed(imageValidation);
        }

        var patch = new
        {
            spec = new
            {
                template = new
                {
                    spec = new
                    {
                        containers = new[]
                        {
                            new
                            {
                                name = container,
                                image
                            }
                        }
                    }
                }
            }
        };

        await client.AppsV1.PatchNamespacedDeploymentAsync(
            new V1Patch(patch, V1Patch.PatchType.StrategicMergePatch),
            name,
            plan.Namespace,
            fieldManager: FieldManager,
            cancellationToken: cancellationToken);

        return ApplyResult.Success(
            $"Updated {K8sConventions.K8sResources.DeploymentDisplayName} {plan.Namespace}/{name} container '{container}' image from '{currentImage}' to '{image}'.");
    }

    private async Task ApplyObjectAsync(IKubernetesObject<V1ObjectMeta> obj, CancellationToken cancellationToken)
    {
        if (await TryApplyDeploymentAsync(obj, cancellationToken))
        {
            return;
        }

        if (await TryApplyServiceAsync(obj, cancellationToken))
        {
            return;
        }

        await TryApplyConfigMapAsync(obj, cancellationToken);
    }

    private async Task<bool> TryApplyDeploymentAsync(
        IKubernetesObject<V1ObjectMeta> obj,
        CancellationToken cancellationToken)
    {
        if (obj is not V1Deployment deployment)
        {
            return false;
        }

        await ApplyDeploymentAsync(deployment, cancellationToken);
        return true;
    }

    private async Task<bool> TryApplyServiceAsync(
        IKubernetesObject<V1ObjectMeta> obj,
        CancellationToken cancellationToken)
    {
        if (obj is not V1Service service)
        {
            return false;
        }

        await ApplyServiceAsync(service, cancellationToken);
        return true;
    }

    private async Task<bool> TryApplyConfigMapAsync(
        IKubernetesObject<V1ObjectMeta> obj,
        CancellationToken cancellationToken)
    {
        if (obj is not V1ConfigMap configMap)
        {
            return false;
        }

        await ApplyConfigMapAsync(configMap, cancellationToken);
        return true;
    }

    private Task ApplyDeploymentAsync(V1Deployment deployment, CancellationToken cancellationToken) =>
        client.AppsV1.PatchNamespacedDeploymentAsync(
            new V1Patch(deployment, V1Patch.PatchType.ApplyPatch),
            deployment.Metadata.Name,
            deployment.Metadata.NamespaceProperty,
            fieldManager: FieldManager,
            force: true,
            cancellationToken: cancellationToken);

    private Task ApplyServiceAsync(V1Service service, CancellationToken cancellationToken) =>
        client.CoreV1.PatchNamespacedServiceAsync(
            new V1Patch(service, V1Patch.PatchType.ApplyPatch),
            service.Metadata.Name,
            service.Metadata.NamespaceProperty,
            fieldManager: FieldManager,
            force: true,
            cancellationToken: cancellationToken);

    private Task ApplyConfigMapAsync(V1ConfigMap configMap, CancellationToken cancellationToken) =>
        client.CoreV1.PatchNamespacedConfigMapAsync(
            new V1Patch(configMap, V1Patch.PatchType.ApplyPatch),
            configMap.Metadata.Name,
            configMap.Metadata.NamespaceProperty,
            fieldManager: FieldManager,
            force: true,
            cancellationToken: cancellationToken);

    private async Task<string> DeleteObjectAsync(K8sObjectRef obj, CancellationToken cancellationToken)
    {
        try
        {
            switch (obj.ApiVersion, obj.Kind)
            {
                case (K8sConventions.K8sResources.AppsV1, K8sConventions.K8sResources.Deployment):
                    await client.AppsV1.DeleteNamespacedDeploymentAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken);
                    break;
                case (K8sConventions.K8sResources.V1, K8sConventions.K8sResources.Service):
                    await client.CoreV1.DeleteNamespacedServiceAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken);
                    break;
                case (K8sConventions.K8sResources.V1, K8sConventions.K8sResources.ConfigMap):
                    await client.CoreV1.DeleteNamespacedConfigMapAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken);
                    break;
                default:
                    return $"Skipped unsupported {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}.";
            }

            return $"Deleted {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return $"Skipped missing {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";
        }
    }

    private async Task<string> WaitForDeploymentsAsync(string namespaceName, IEnumerable<string> deploymentNames, CancellationToken cancellationToken)
    {
        var names = deploymentNames.Distinct(StringComparer.Ordinal).ToArray();
        if (names.Length == 0)
        {
            return "No Deployments to wait for.";
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        return await WaitForDeploymentRolloutsAsync(namespaceName, names, deadline, cancellationToken);
    }

    private async Task<string> WaitForDeploymentRolloutsAsync(
        string namespaceName,
        string[] names,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        while (DateTimeOffset.UtcNow < deadline)
        {
            var pending = await ReadPendingDeploymentRolloutsAsync(namespaceName, names, cancellationToken);

            if (pending.Count == 0)
            {
                return $"Deployment rollout completed for {string.Join(", ", names)}.";
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return $"Timed out waiting for Deployment rollout: {string.Join(", ", names)}.";
    }

    private async Task<List<string>> ReadPendingDeploymentRolloutsAsync(
        string namespaceName,
        IEnumerable<string> names,
        CancellationToken cancellationToken)
    {
        var pending = new List<string>();
        foreach (var name in names)
        {
            var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(
                name,
                namespaceName,
                cancellationToken: cancellationToken);
            var rollout = DeploymentRolloutStatus.From(deployment);
            if (!rollout.IsComplete)
            {
                pending.Add(rollout.PendingMessage(name));
            }
        }

        return pending;
    }

    private static V1Container? FindDeploymentContainer(V1Deployment deployment, string container) =>
        deployment.Spec?.Template?.Spec?.Containers?
            .FirstOrDefault(item => string.Equals(item.Name, container, StringComparison.Ordinal));

    private static string? ValidatePlannedContainerImage(
        string namespaceName,
        string deploymentName,
        string container,
        string currentImage,
        V1Container? deploymentContainer)
    {
        if (deploymentContainer is null)
        {
            return $"Deployment '{namespaceName}/{deploymentName}' does not contain container '{container}'. Re-request the plan.";
        }

        var actualImage = deploymentContainer.Image ?? string.Empty;
        return string.Equals(actualImage, currentImage, StringComparison.Ordinal)
            ? null
            : $"Deployment '{namespaceName}/{deploymentName}' container '{container}' image changed from planned '{currentImage}' to '{actualImage}'. Re-request the plan.";
    }

    private sealed record DeploymentRolloutStatus(
        bool IsComplete,
        int Desired,
        int Updated,
        int Ready,
        int Available)
    {
        public static DeploymentRolloutStatus From(V1Deployment deployment)
        {
            var desired = DesiredReplicas(deployment);
            var updated = UpdatedReplicas(deployment);
            var ready = ReadyReplicas(deployment);
            var available = AvailableReplicas(deployment);
            var isComplete = IsRolloutComplete(deployment, desired, updated, ready, available);

            return new DeploymentRolloutStatus(isComplete, desired, updated, ready, available);
        }

        private static int DesiredReplicas(V1Deployment deployment) =>
            deployment.Spec?.Replicas ?? 0;

        private static int UpdatedReplicas(V1Deployment deployment) =>
            deployment.Status?.UpdatedReplicas ?? 0;

        private static int ReadyReplicas(V1Deployment deployment) =>
            deployment.Status?.ReadyReplicas ?? 0;

        private static int AvailableReplicas(V1Deployment deployment) =>
            deployment.Status?.AvailableReplicas ?? 0;

        private static bool IsRolloutComplete(
            V1Deployment deployment,
            int desired,
            int updated,
            int ready,
            int available) =>
            HasObservedGeneration(deployment) &&
            HasExpectedReplicaCounts(desired, updated, ready, available);

        private static bool HasObservedGeneration(V1Deployment deployment) =>
            (deployment.Status?.ObservedGeneration ?? 0) >= (deployment.Metadata?.Generation ?? 0);

        private static bool HasExpectedReplicaCounts(int desired, int updated, int ready, int available) =>
            updated == desired &&
            ready == desired &&
            available == desired;

        public string PendingMessage(string name) =>
            $"{name}: desired={Desired}, updated={Updated}, ready={Ready}, available={Available}";
    }

    private sealed class PlanApprovalInput
    {
        [Description("Set to true to approve applying this Kubernetes plan.")]
        public bool Approve { get; set; }

        [Description("Echo the PlanId from the approval prompt.")]
        public string PlanId { get; set; } = string.Empty;
    }
}
