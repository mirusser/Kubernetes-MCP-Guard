using System.Text.Json;
using InfraGate.Approvals;

namespace InfraGate.KubernetesAdapter;

public sealed class KubernetesPlanExecutor(IToolCaller toolCaller) : IDomainPlanExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct)
    {
        var decodeResult = KubernetesApprovalAdapter.Decode(envelope);
        if (!decodeResult.Succeeded || decodeResult.Plan is null)
        {
            return DomainPlanExecutionResult.Blocked(decodeResult.Message);
        }

        var plan = decodeResult.Plan;
        var payload = plan.Payload;

        var driftBlock = await CheckLiveDriftAsync(plan, payload, ct);
        if (driftBlock is not null)
        {
            var audit = ApplyDriftDetectedAudit(plan, driftBlock, payload);
            return DomainPlanExecutionResult.Blocked(driftBlock, audit);
        }

        var dryRunBlock = await RunPreExecuteDryRunAsync(plan, payload, ct);
        if (dryRunBlock is not null)
        {
            var audit = DryRunFailedAudit(plan, dryRunBlock, payload);
            return DomainPlanExecutionResult.Blocked(dryRunBlock, audit);
        }

        return DomainPlanExecutionResult.Success("Pre-execution checks passed.", payload.Namespace);
    }

    public async Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct)
    {
        var decodeResult = KubernetesApprovalAdapter.Decode(envelope);
        if (!decodeResult.Succeeded || decodeResult.Plan is null)
        {
            return DomainPlanExecutionResult.Blocked(decodeResult.Message);
        }

        var plan = decodeResult.Plan;
        var payload = plan.Payload;

        return await DispatchAsync(plan.Operation, payload, ct);
    }

    private async Task<string?> CheckLiveDriftAsync(KubernetesPlan plan, KubernetesPlanPayload payload, CancellationToken ct)
    {
        if (payload.Diffs.Length == 0)
        {
            return null;
        }

        var diffsJson = JsonSerializer.Serialize(payload.Diffs, JsonOptions);
        var result = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                [KubernetesAdapterConventions.EvidenceArguments.Operation] = plan.Operation,
                [KubernetesAdapterConventions.EvidenceArguments.DiffsJson] = diffsJson
            },
            ct).ConfigureAwait(false);

        return string.Equals(result, KubernetesAdapterConventions.DriftCheckResults.NoDrift, StringComparison.Ordinal)
            ? null
            : $"Plan '{plan.Id}' cannot be executed: live Kubernetes state has drifted. {result}";
    }

    private async Task<string?> RunPreExecuteDryRunAsync(KubernetesPlan plan, KubernetesPlanPayload payload, CancellationToken ct) =>
        plan.Operation switch
        {
            KubernetesAdapterConventions.PlanOperations.Apply =>
                await CheckApplyDryRunAsync(plan.Id, payload, ct),
            KubernetesAdapterConventions.PlanOperations.Delete =>
                await CheckSimpleDryRunAsync(
                    KubernetesAdapterConventions.EvidenceTools.DryRunDeleteManifest,
                    new Dictionary<string, object?>
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Manifest] = payload.Manifest ?? string.Empty
                    },
                    plan.Id,
                    ct),
            KubernetesAdapterConventions.PlanOperations.Scale =>
                await CheckSimpleDryRunAsync(
                    KubernetesAdapterConventions.EvidenceTools.DryRunScaleDeployment,
                    new Dictionary<string, object?>
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty),
                        [KubernetesAdapterConventions.EvidenceArguments.Replicas] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Replicas, "0")
                    },
                    plan.Id,
                    ct),
            KubernetesAdapterConventions.PlanOperations.Restart =>
                await CheckSimpleDryRunAsync(
                    KubernetesAdapterConventions.EvidenceTools.DryRunRestartDeployment,
                    new Dictionary<string, object?>
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty)
                    },
                    plan.Id,
                    ct),
            KubernetesAdapterConventions.PlanOperations.SetImage =>
                await CheckSimpleDryRunAsync(
                    KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage,
                    new Dictionary<string, object?>
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty),
                        [KubernetesAdapterConventions.EvidenceArguments.Container] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Container, string.Empty),
                        [KubernetesAdapterConventions.EvidenceArguments.Image] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Image, string.Empty)
                    },
                    plan.Id,
                    ct),
            _ => null
        };

    private async Task<string?> CheckApplyDryRunAsync(string planId, KubernetesPlanPayload payload, CancellationToken ct)
    {
        var evidenceJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = payload.Manifest ?? string.Empty
            },
            ct).ConfigureAwait(false);

        K8sApplyEvidence? evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<K8sApplyEvidence>(evidenceJson, JsonOptions);
        }
        catch (JsonException)
        {
            return $"Pre-execute dry-run failed for plan '{planId}': {evidenceJson}";
        }

        if (evidence is null)
        {
            return $"Pre-execute dry-run for plan '{planId}' returned an empty result.";
        }

        if (evidence.PolicyBlocked)
        {
            return $"Plan '{planId}' blocked by policy:{Environment.NewLine}{evidence.PolicyRefusal}";
        }

        return null;
    }

    private async Task<string?> CheckSimpleDryRunAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        string planId,
        CancellationToken ct)
    {
        var dryRunJson = await toolCaller.CallAsync(toolName, arguments, ct).ConfigureAwait(false);

        K8sPlanDryRun? dryRun;
        try
        {
            dryRun = JsonSerializer.Deserialize<K8sPlanDryRun>(dryRunJson, JsonOptions);
        }
        catch (JsonException)
        {
            return $"Pre-execute dry-run failed for plan '{planId}': {dryRunJson}";
        }

        return dryRun is null
            ? $"Pre-execute dry-run failed for plan '{planId}': {dryRunJson}"
            : null;
    }

    private async Task<DomainPlanExecutionResult> DispatchAsync(string operation, KubernetesPlanPayload payload, CancellationToken ct)
    {
        var message = await DispatchMutationAsync(operation, payload, ct).ConfigureAwait(false);

        return IsUnsupportedOperationMessage(message)
            ? DomainPlanExecutionResult.Blocked(message)
            : DomainPlanExecutionResult.Success(message, payload.Namespace);
    }

    private Task<string> DispatchMutationAsync(string operation, KubernetesPlanPayload payload, CancellationToken ct) =>
        operation switch
        {
            KubernetesAdapterConventions.PlanOperations.Apply =>
                toolCaller.CallAsync(
                    KubernetesAdapterConventions.MutationTools.ApplyManifest,
                    new Dictionary<string, object?>
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Manifest] = payload.Manifest ?? string.Empty
                    },
                    ct),
            KubernetesAdapterConventions.PlanOperations.Delete =>
                toolCaller.CallAsync(
                    KubernetesAdapterConventions.MutationTools.DeleteManifest,
                    new Dictionary<string, object?>
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Manifest] = payload.Manifest ?? string.Empty
                    },
                    ct),
            KubernetesAdapterConventions.PlanOperations.Scale =>
                toolCaller.CallAsync(
                    KubernetesAdapterConventions.MutationTools.ScaleDeployment,
                    new Dictionary<string, object?>
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty),
                        [KubernetesAdapterConventions.EvidenceArguments.Replicas] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Replicas, "0")
                    },
                    ct),
            KubernetesAdapterConventions.PlanOperations.Restart =>
                toolCaller.CallAsync(
                    KubernetesAdapterConventions.MutationTools.RestartDeployment,
                    new Dictionary<string, object?>
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty)
                    },
                    ct),
            KubernetesAdapterConventions.PlanOperations.SetImage =>
                toolCaller.CallAsync(
                    KubernetesAdapterConventions.MutationTools.SetDeploymentImage,
                    new Dictionary<string, object?>
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty),
                        [KubernetesAdapterConventions.EvidenceArguments.Container] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Container, string.Empty),
                        [KubernetesAdapterConventions.EvidenceArguments.Image] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Image, string.Empty)
                    },
                    ct),
            _ => Task.FromResult($"Unsupported operation '{operation}'.")
        };

    private static bool IsUnsupportedOperationMessage(string message) =>
        message.StartsWith("Unsupported operation ", StringComparison.Ordinal);

    private static PlanAudit DryRunFailedAudit(KubernetesPlan plan, string message, KubernetesPlanPayload payload) =>
        new(
            ApprovalConventions.AuditEvents.DryRunFailed,
            new InfraGate.Approvals.AuditPayloads.DryRunFailedPayload(
                "pre-apply",
                plan.Id,
                plan.Operation,
                payload.Namespace,
                payload.Objects.Select(obj => $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}").ToArray(),
                message));

    private static PlanAudit ApplyDriftDetectedAudit(KubernetesPlan plan, string message, KubernetesPlanPayload payload) =>
        new(
            ApprovalConventions.AuditEvents.ApplyDriftDetected,
            new InfraGate.Approvals.AuditPayloads.ApplyDriftDetectedPayload(
                plan.Id,
                plan.Operation,
                payload.Namespace,
                message));
}
