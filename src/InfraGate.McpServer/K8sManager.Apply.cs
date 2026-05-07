using InfraGate.Approvals;
using InfraGate.McpServer.Diff;
using InfraGate.McpServer.Policy;
using k8s;
using k8s.Models;

namespace InfraGate.McpServer;

public sealed partial class K8sManager
{
    public async Task<string> ApplyApprovedPlanAsync(string planId, CancellationToken cancellationToken)
    {
        var approved = await approvalStore.GetApprovedPlanAsync(planId, cancellationToken);
        if (!approved.IsApproved || approved.Plan is null || approved.Hash is null)
        {
            await approvalStore.WriteAuditAsync(ApprovalConventions.AuditEvents.ApplyDenied, new
            {
                planId,
                approved.Message
            }, cancellationToken);

            return $"Refused: {approved.Message}";
        }

        var applyResult = await ApplyPlanAsync(approved.Plan, cancellationToken);
        if (!applyResult.Succeeded)
        {
            await approvalStore.WriteAuditAsync(ApprovalConventions.AuditEvents.ApplyFailed, new
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

    private async Task<ApplyResult> ApplyPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        if (plan.DryRun is null)
        {
            return ApplyResult.Failed($"Plan '{plan.Id}' is missing recorded server-side dry-run data. Re-request the plan.");
        }

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

    private async Task<ApplyResult?> RefuseIfPlanDiffsMissingOrLiveDriftedAsync(
        K8sPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.Diffs.Length == 0)
        {
            return ApplyResult.Failed($"Plan '{plan.Id}' is missing recorded diff data. Re-request the plan.");
        }

        var drift = await K8sDiffService.FindDriftAsync(client, plan, cancellationToken);
        if (drift is null)
        {
            return null;
        }

        await approvalStore.WriteAuditAsync(ApprovalConventions.AuditEvents.ApplyDriftDetected, new
        {
            plan.Id,
            plan.Operation,
            plan.Namespace,
            message = drift
        }, cancellationToken);

        return ApplyResult.Failed($"Live Kubernetes state changed after approval; refusing to mutate Kubernetes.{Environment.NewLine}{drift}");
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

        var policyResult = K8sPolicyValidator.Validate(parsed.Objects, K8sPolicyOptions.Default);
        if (policyResult.IsDenied)
        {
            return ApplyResult.Failed(
                $"Apply refused by policy (re-validated at apply time):{Environment.NewLine}{policyResult.FormatRefusal()}");
        }

        var driftRefusal = await RefuseIfPlanDiffsMissingOrLiveDriftedAsync(plan, cancellationToken);
        if (driftRefusal is not null)
        {
            return driftRefusal;
        }

        var dryRunRefusal = await RefuseIfPreApplyDryRunFailsAsync(
            plan,
            DryRunApplyManifestAsync(parsed.Objects, cancellationToken),
            cancellationToken);
        if (dryRunRefusal is not null)
        {
            return dryRunRefusal;
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
        var driftRefusal = await RefuseIfPlanDiffsMissingOrLiveDriftedAsync(plan, cancellationToken);
        if (driftRefusal is not null)
        {
            return driftRefusal;
        }

        var dryRunRefusal = await RefuseIfPreApplyDryRunFailsAsync(
            plan,
            DryRunDeleteManifestAsync(plan.Objects, cancellationToken),
            cancellationToken);
        if (dryRunRefusal is not null)
        {
            return dryRunRefusal;
        }

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
        var driftRefusal = await RefuseIfPlanDiffsMissingOrLiveDriftedAsync(plan, cancellationToken);
        if (driftRefusal is not null)
        {
            return driftRefusal;
        }

        var dryRunRefusal = await RefuseIfPreApplyDryRunFailsAsync(
            plan,
            DryRunScaleDeploymentAsync(plan.Namespace, name, replicas, cancellationToken),
            cancellationToken);
        if (dryRunRefusal is not null)
        {
            return dryRunRefusal;
        }

        await client.AppsV1.PatchNamespacedDeploymentScaleAsync(
            CreateScaleDeploymentPatch(replicas),
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
        var driftRefusal = await RefuseIfPlanDiffsMissingOrLiveDriftedAsync(plan, cancellationToken);
        if (driftRefusal is not null)
        {
            return driftRefusal;
        }

        var dryRunRefusal = await RefuseIfPreApplyDryRunFailsAsync(
            plan,
            DryRunRestartDeploymentAsync(plan.Namespace, name, restartedAtUtc, cancellationToken),
            cancellationToken);
        if (dryRunRefusal is not null)
        {
            return dryRunRefusal;
        }

        await client.AppsV1.PatchNamespacedDeploymentAsync(
            CreateRestartDeploymentPatch(restartedAtUtc),
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

        var driftRefusal = await RefuseIfPlanDiffsMissingOrLiveDriftedAsync(plan, cancellationToken);
        if (driftRefusal is not null)
        {
            return driftRefusal;
        }

        var dryRunRefusal = await RefuseIfPreApplyDryRunFailsAsync(
            plan,
            DryRunSetDeploymentImageAsync(plan.Namespace, name, container, image, cancellationToken),
            cancellationToken);
        if (dryRunRefusal is not null)
        {
            return dryRunRefusal;
        }

        await client.AppsV1.PatchNamespacedDeploymentAsync(
            CreateSetDeploymentImagePatch(container, image),
            name,
            plan.Namespace,
            fieldManager: FieldManager,
            cancellationToken: cancellationToken);

        return ApplyResult.Success(
            $"Updated {K8sConventions.K8sResources.DeploymentDisplayName} {plan.Namespace}/{name} container '{container}' image from '{currentImage}' to '{image}'.");
    }

    private async Task<ApplyResult?> RefuseIfPreApplyDryRunFailsAsync(
        K8sPlan plan,
        Task<DryRunResult> dryRunTask,
        CancellationToken cancellationToken)
    {
        var dryRun = await dryRunTask;
        if (dryRun.Succeeded)
        {
            return null;
        }

        await WriteDryRunFailedAuditAsync(
            K8sConventions.DryRunPhases.Apply,
            plan,
            dryRun.Message,
            cancellationToken);

        return ApplyResult.Failed(FormatApplyDryRunRefusal(dryRun.Message));
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

}
