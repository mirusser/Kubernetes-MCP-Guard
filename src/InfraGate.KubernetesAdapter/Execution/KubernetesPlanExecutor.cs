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

public sealed class KubernetesPlanExecutor : IDomainPlanExecutor
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IToolCaller toolCaller;
    private readonly IKubernetesEvidenceService evidenceService;
    private readonly IApprovalAuditOutbox auditOutbox;

    internal KubernetesPlanExecutor(
        IToolCaller toolCaller,
        IKubernetesEvidenceService evidenceService,
        IApprovalAuditOutbox? auditOutbox = null)
    {
        ArgumentNullException.ThrowIfNull(toolCaller);
        ArgumentNullException.ThrowIfNull(evidenceService);

        this.toolCaller = toolCaller;
        this.evidenceService = evidenceService;
        this.auditOutbox = auditOutbox ?? NullApprovalAuditOutbox.Instance;
    }

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
            var audit = KubernetesAuditHelper.ApplyDriftDetected(
                plan.Id,
                plan.Operation,
                payload.Namespace,
                driftBlock.Message);
            return DomainPlanExecutionResult.Blocked(driftBlock.Message, audit, driftBlock.ReasonCode);
        }

        var resourceVersionBlock = await CheckResourceVersionAsync(plan, payload, ct).ConfigureAwait(false);
        if (resourceVersionBlock is not null)
        {
            var audit = KubernetesAuditHelper.ApplyDriftDetected(
                plan.Id,
                plan.Operation,
                payload.Namespace,
                resourceVersionBlock.Message);
            return DomainPlanExecutionResult.Blocked(resourceVersionBlock.Message, audit, resourceVersionBlock.ReasonCode);
        }

        var storedPolicyBlock = CheckStoredPolicyFindings(plan, payload);
        if (storedPolicyBlock is not null)
        {
            return DomainPlanExecutionResult.Blocked(
                storedPolicyBlock.Message,
                KubernetesAuditHelper.ApplyDenied(plan.Id, storedPolicyBlock.Message),
                storedPolicyBlock.ReasonCode);
        }

        var policyBlock = CheckSetDeploymentImagePolicy(plan, payload);
        if (policyBlock is not null)
        {
            return DomainPlanExecutionResult.Blocked(
                policyBlock.Message,
                KubernetesAuditHelper.ApplyDenied(plan.Id, policyBlock.Message),
                policyBlock.ReasonCode);
        }

        var dryRunBlock = await RunPreExecuteDryRunAsync(plan, payload, ct).ConfigureAwait(false);
        if (dryRunBlock is not null)
        {
            var audit = KubernetesAuditHelper.DryRunFailed(
                KubernetesAdapterConventions.AuditPhases.PreApply,
                plan.Id,
                plan.Operation,
                payload.Namespace,
                FormatObjects(payload),
                dryRunBlock.Message);
            return DomainPlanExecutionResult.Blocked(dryRunBlock.Message, audit, dryRunBlock.ReasonCode);
        }

        await auditOutbox.AppendAsync(
            new ApprovalAuditEntry(
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
                        jsonOptions)),
                PlanId: plan.Id),
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

        await auditOutbox.AppendAsync(
            new ApprovalAuditEntry(
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
                        jsonOptions)),
                PlanId: plan.Id),
            ct).ConfigureAwait(false);

        return await DispatchAsync(plan.Operation, payload, ct).ConfigureAwait(false);
    }

    private async Task<ResultFailure?> CheckResourceVersionAsync(KubernetesPlan plan, KubernetesPlanPayload payload, CancellationToken ct)
    {
        var resourceVersionCheck = plan.Envelope.FreshnessPolicy.Checks
            .FirstOrDefault(c => string.Equals(c.Type, KubernetesAdapterConventions.FreshnessCheckTypes.ResourceVersionCheck, StringComparison.Ordinal));

        if (resourceVersionCheck is null || resourceVersionCheck.Parameters.Count == 0)
        {
            return null;
        }

        var parametersJson = JsonSerializer.Serialize(resourceVersionCheck.Parameters, jsonOptions);
        var result = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.CheckResourceVersion,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
                [KubernetesAdapterConventions.EvidenceArguments.DiffsJson] = parametersJson
            },
            ct).ConfigureAwait(false);

        return string.Equals(result, KubernetesAdapterConventions.DriftCheckResults.NoDrift, StringComparison.Ordinal)
            ? null
            : new ResultFailure(
                $"Plan '{plan.Id}' cannot be executed: live resource versions have changed. {result}",
                KubernetesAdapterConventions.ResultReasonCodes.ResourceVersionMismatch);
    }

    private async Task<ResultFailure?> CheckLiveDriftAsync(KubernetesPlan plan, KubernetesPlanPayload payload, CancellationToken ct)
    {
        var hasLiveDriftCheck = plan.Envelope.FreshnessPolicy.Checks
            .Any(c => string.Equals(c.Type, KubernetesAdapterConventions.FreshnessCheckTypes.LiveDrift, StringComparison.Ordinal));

        if (!hasLiveDriftCheck || payload.Diffs.Length == 0)
        {
            return null;
        }

        var diffsJson = JsonSerializer.Serialize(payload.Diffs, jsonOptions);
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

    private async Task<ResultFailure?> RunPreExecuteDryRunAsync(KubernetesPlan plan, KubernetesPlanPayload payload, CancellationToken ct)
    {
        if (!OperationDispatchMap.TryGetValue(plan.Operation, out var dispatch) || dispatch is null)
        {
            return null;
        }

        if (plan.Operation is KubernetesAdapterConventions.PlanOperations.Apply)
        {
            var evidence = await evidenceService.CheckApplyDryRunAsync(
                payload.Namespace,
                payload.Manifest ?? string.Empty,
                ct).ConfigureAwait(false);

            if (evidence is null)
            {
                return new ResultFailure(
                    $"Pre-execute dry-run for plan '{plan.Id}' returned an empty result.",
                    KubernetesAdapterConventions.ResultReasonCodes.PreExecuteDryRunFailed);
            }

            return evidence.PolicyBlocked
                ? new ResultFailure(
                    $"Plan '{plan.Id}' blocked by policy:{Environment.NewLine}{evidence.PolicyRefusal}",
                    KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked)
                : null;
        }

        var dryRun = await evidenceService.GetDryRunAsync(
            dispatch.DryRunTool,
            dispatch.ArgsBuilder(payload),
            ct).ConfigureAwait(false);

        return dryRun is null
            ? new ResultFailure(
                $"Pre-execute dry-run failed for plan '{plan.Id}'.",
                KubernetesAdapterConventions.ResultReasonCodes.PreExecuteDryRunFailed)
            : null;
    }

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

    private static ResultFailure? CheckStoredPolicyFindings(KubernetesPlan plan, KubernetesPlanPayload payload)
    {
        var deniedFindings = payload.PolicyFindings
            .Where(f => string.Equals(f.Severity, KubernetesAdapterConventions.PolicySeverities.Deny, StringComparison.Ordinal))
            .ToArray();

        return deniedFindings.Length == 0
            ? null
            : new ResultFailure(
                $"Plan '{plan.Id}' blocked by policy:{Environment.NewLine}{FormatPolicyFindings(deniedFindings)}",
                KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked);
    }

    private static string FormatPolicyFindings(IReadOnlyList<KubernetesPlanPolicyFinding> findings) =>
        string.Join(
            Environment.NewLine,
            findings.Select(f => $"[{f.Code}] {f.ObjectRef}: {f.Message}"));

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
        OperationDispatchMap.TryGetValue(operation, out var dispatch) && dispatch is not null
            ? toolCaller.CallAsync(dispatch.MutationTool, dispatch.ArgsBuilder(payload), ct)
            : Task.FromResult($"Unsupported operation '{operation}'.");

    private static bool IsUnsupportedOperationMessage(string message) =>
        message.StartsWith("Unsupported operation ", StringComparison.Ordinal);

    private static string[] FormatObjects(KubernetesPlanPayload payload) =>
        payload.Objects.Select(obj => $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}").ToArray();

    private sealed record class ResultFailure(string Message, string ReasonCode);

    private sealed class NullApprovalAuditOutbox : IApprovalAuditOutbox
    {
        public static readonly NullApprovalAuditOutbox Instance = new();

        public Task<long> AppendAsync(ApprovalAuditEntry entry, CancellationToken cancellationToken) =>
            Task.FromResult(0L);
    }
}
