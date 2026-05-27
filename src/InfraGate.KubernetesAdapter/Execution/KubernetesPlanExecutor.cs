using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.AuditPayloads;
using InfraGate.KubernetesAdapter.Policy;
using InfraGate.KubernetesAdapter.Approval;
using InfraGate.KubernetesAdapter.Evidence;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Execution;

public sealed class KubernetesPlanExecutor(
    IToolCaller toolCaller,
    IApprovalAuditPublisher? auditPublisher = null) : IDomainPlanExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IApprovalAuditPublisher auditPublisher = auditPublisher ?? NoOpApprovalAuditPublisher.Instance;

    public async Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct)
    {
        var decodeResult = KubernetesApprovalAdapter.Decode(envelope);
        if (!decodeResult.Succeeded || decodeResult.Plan is null)
        {
            return DomainPlanExecutionResult.Blocked(decodeResult.Message, decodeResult.ReasonCode);
        }

        var plan = decodeResult.Plan;
        var payload = plan.Payload;

        var driftBlock = await CheckLiveDriftAsync(plan, payload, ct).ConfigureAwait(false);
        if (driftBlock is not null)
        {
            var audit = ApplyDriftDetectedAudit(plan, driftBlock.Message, payload);
            return DomainPlanExecutionResult.Blocked(driftBlock.Message, audit, driftBlock.ReasonCode);
        }

        var policyBlock = CheckSetDeploymentImagePolicy(plan, payload);
        if (policyBlock is not null)
        {
            return DomainPlanExecutionResult.Blocked(
                policyBlock.Message,
                new PlanAudit(
                    ApprovalConventions.AuditEvents.ApplyDenied,
                    new ApplyDeniedPayload(plan.Id, policyBlock.Message)),
                policyBlock.ReasonCode);
        }

        var dryRunBlock = await RunPreExecuteDryRunAsync(plan, payload, ct).ConfigureAwait(false);
        if (dryRunBlock is not null)
        {
            var audit = DryRunFailedAudit(plan, dryRunBlock.Message, payload);
            return DomainPlanExecutionResult.Blocked(dryRunBlock.Message, audit, dryRunBlock.ReasonCode);
        }

        await auditPublisher.PublishAsync(
            new PlanAudit(
                ApprovalConventions.AuditEvents.PreExecutionChecked,
                new PreExecutionCheckedPayload(
                    plan.Id,
                    plan.Operation,
                    KubernetesAdapterConventions.AdapterId,
                    JsonSerializer.SerializeToElement(
                        new KubernetesPreExecutionCheckedAdapterPayload(
                            payload.Namespace,
                            FormatObjects(payload),
                            plan.Envelope.FreshnessPolicy.Checks.Select(check => check.Type).ToArray()),
                        JsonOptions))),
            ct).ConfigureAwait(false);

        return DomainPlanExecutionResult.Success("Pre-execution checks passed.", payload.Namespace);
    }

    public async Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct)
    {
        var decodeResult = KubernetesApprovalAdapter.Decode(envelope);
        if (!decodeResult.Succeeded || decodeResult.Plan is null)
        {
            return DomainPlanExecutionResult.Blocked(decodeResult.Message, decodeResult.ReasonCode);
        }

        var plan = decodeResult.Plan;
        var payload = plan.Payload;

        await auditPublisher.PublishAsync(
            new PlanAudit(
                ApprovalConventions.AuditEvents.ExecutionStarted,
                new ExecutionStartedPayload(
                    plan.Id,
                    plan.Operation,
                    KubernetesAdapterConventions.AdapterId,
                    JsonSerializer.SerializeToElement(
                        new KubernetesExecutionStartedAdapterPayload(
                            payload.Namespace,
                            FormatObjects(payload),
                            payload.Parameters),
                        JsonOptions))),
            ct).ConfigureAwait(false);

        return await DispatchAsync(plan.Operation, payload, ct).ConfigureAwait(false);
    }

    private async Task<ResultFailure?> CheckLiveDriftAsync(KubernetesPlan plan, KubernetesPlanPayload payload, CancellationToken ct)
    {
        if (payload.Diffs.Length == 0)
        {
            return null;
        }

        var diffsJson = JsonSerializer.Serialize(payload.Diffs, JsonOptions);
        var result = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.CheckLiveDrift,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                [KubernetesAdapterConventions.EvidenceArguments.Operation] = plan.Operation,
                [KubernetesAdapterConventions.EvidenceArguments.DiffsJson] = diffsJson
            },
            ct).ConfigureAwait(false);

        return string.Equals(result, KubernetesAdapterConventions.DriftCheckResults.NoDrift, StringComparison.Ordinal)
            ? null
            : new ResultFailure(
                $"Plan '{plan.Id}' cannot be executed: live Kubernetes state has drifted. {result}",
                KubernetesAdapterConventions.ResultReasonCodes.LiveDrift);
    }

    private async Task<ResultFailure?> RunPreExecuteDryRunAsync(KubernetesPlan plan, KubernetesPlanPayload payload, CancellationToken ct) =>
        plan.Operation switch
        {
            KubernetesAdapterConventions.PlanOperations.Apply =>
                await CheckApplyDryRunAsync(plan.Id, payload, ct).ConfigureAwait(false),
            KubernetesAdapterConventions.PlanOperations.Delete =>
                await CheckSimpleDryRunAsync(
                    KubernetesAdapterConventions.EvidenceTools.DryRunDeleteManifest,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Manifest] = payload.Manifest ?? string.Empty
                    },
                    plan.Id,
                    ct).ConfigureAwait(false),
            KubernetesAdapterConventions.PlanOperations.Scale =>
                await CheckSimpleDryRunAsync(
                    KubernetesAdapterConventions.EvidenceTools.DryRunScaleDeployment,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty),
                        [KubernetesAdapterConventions.EvidenceArguments.Replicas] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Replicas, "0")
                    },
                    plan.Id,
                    ct).ConfigureAwait(false),
            KubernetesAdapterConventions.PlanOperations.Restart =>
                await CheckSimpleDryRunAsync(
                    KubernetesAdapterConventions.EvidenceTools.DryRunRestartDeployment,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty)
                    },
                    plan.Id,
                    ct).ConfigureAwait(false),
            KubernetesAdapterConventions.PlanOperations.SetImage =>
                await CheckSimpleDryRunAsync(
                    KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty),
                        [KubernetesAdapterConventions.EvidenceArguments.Container] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Container, string.Empty),
                        [KubernetesAdapterConventions.EvidenceArguments.Image] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Image, string.Empty)
                    },
                    plan.Id,
                    ct).ConfigureAwait(false),
            _ => null
        };

    private static ResultFailure? CheckSetDeploymentImagePolicy(KubernetesPlan plan, KubernetesPlanPayload payload)
    {
        if (plan.Operation is not KubernetesAdapterConventions.PlanOperations.SetImage)
        {
            return null;
        }

        var policyResult = KubernetesPolicyValidator.ValidateSetDeploymentImage(
            payload.Namespace,
            payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty),
            payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Container, string.Empty),
            payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Image, string.Empty),
            KubernetesPolicyOptions.Default);

        return policyResult.IsDenied
            ? new ResultFailure(
                $"Plan '{plan.Id}' blocked by policy:{Environment.NewLine}{policyResult.FormatRefusal()}",
                KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked)
            : null;
    }

    private async Task<ResultFailure?> CheckApplyDryRunAsync(string planId, KubernetesPlanPayload payload, CancellationToken ct)
    {
        var evidenceJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = payload.Manifest ?? string.Empty
            },
            ct).ConfigureAwait(false);

        KubernetesApplyEvidence? evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<KubernetesApplyEvidence>(evidenceJson, JsonOptions);
        }
        catch (JsonException)
        {
            return new ResultFailure(
                $"Pre-execute dry-run failed for plan '{planId}': {evidenceJson}",
                KubernetesAdapterConventions.ResultReasonCodes.PreExecuteDryRunFailed);
        }

        if (evidence is null)
        {
            return new ResultFailure(
                $"Pre-execute dry-run for plan '{planId}' returned an empty result.",
                KubernetesAdapterConventions.ResultReasonCodes.PreExecuteDryRunFailed);
        }

        if (evidence.PolicyBlocked)
        {
            return new ResultFailure(
                $"Plan '{planId}' blocked by policy:{Environment.NewLine}{evidence.PolicyRefusal}",
                KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked);
        }

        return null;
    }

    private async Task<ResultFailure?> CheckSimpleDryRunAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        string planId,
        CancellationToken ct)
    {
        var dryRunJson = await toolCaller.CallAsync(toolName, arguments, ct).ConfigureAwait(false);

        KubernetesPlanDryRun? dryRun;
        try
        {
            dryRun = JsonSerializer.Deserialize<KubernetesPlanDryRun>(dryRunJson, JsonOptions);
        }
        catch (JsonException)
        {
            return new ResultFailure(
                $"Pre-execute dry-run failed for plan '{planId}': {dryRunJson}",
                KubernetesAdapterConventions.ResultReasonCodes.PreExecuteDryRunFailed);
        }

        return dryRun is null
            ? new ResultFailure(
                $"Pre-execute dry-run failed for plan '{planId}': {dryRunJson}",
                KubernetesAdapterConventions.ResultReasonCodes.PreExecuteDryRunFailed)
            : null;
    }

    private async Task<DomainPlanExecutionResult> DispatchAsync(string operation, KubernetesPlanPayload payload, CancellationToken ct)
    {
        var message = await DispatchMutationAsync(operation, payload, ct).ConfigureAwait(false);

        return IsUnsupportedOperationMessage(message)
            ? DomainPlanExecutionResult.Blocked(
                message,
                KubernetesAdapterConventions.ResultReasonCodes.UnsupportedOperation)
            : DomainPlanExecutionResult.Success(message, payload.Namespace);
    }

    private Task<string> DispatchMutationAsync(string operation, KubernetesPlanPayload payload, CancellationToken ct) =>
        operation switch
        {
            KubernetesAdapterConventions.PlanOperations.Apply =>
                toolCaller.CallAsync(
                    KubernetesAdapterConventions.MutationTools.ApplyManifest,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Manifest] = payload.Manifest ?? string.Empty
                    },
                    ct),
            KubernetesAdapterConventions.PlanOperations.Delete =>
                toolCaller.CallAsync(
                    KubernetesAdapterConventions.MutationTools.DeleteManifest,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Manifest] = payload.Manifest ?? string.Empty
                    },
                    ct),
            KubernetesAdapterConventions.PlanOperations.Scale =>
                toolCaller.CallAsync(
                    KubernetesAdapterConventions.MutationTools.ScaleDeployment,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty),
                        [KubernetesAdapterConventions.EvidenceArguments.Replicas] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Replicas, "0")
                    },
                    ct),
            KubernetesAdapterConventions.PlanOperations.Restart =>
                toolCaller.CallAsync(
                    KubernetesAdapterConventions.MutationTools.RestartDeployment,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                        [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty)
                    },
                    ct),
            KubernetesAdapterConventions.PlanOperations.SetImage =>
                toolCaller.CallAsync(
                    KubernetesAdapterConventions.MutationTools.SetDeploymentImage,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
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

    private static string[] FormatObjects(KubernetesPlanPayload payload) =>
        payload.Objects.Select(obj => $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}").ToArray();

    private sealed record class ResultFailure(string Message, string ReasonCode);
}
