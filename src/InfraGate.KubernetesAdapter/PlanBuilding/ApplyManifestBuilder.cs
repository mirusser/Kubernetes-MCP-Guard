using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter.Evidence;
using static InfraGate.KubernetesAdapter.PlanBuilding.KubernetesBuilderInfrastructure;

namespace InfraGate.KubernetesAdapter.PlanBuilding;

internal sealed class ApplyManifestBuilder(IKubernetesEvidenceService evidenceService) : IOperationPlanBuilder
{
    private readonly IKubernetesEvidenceService evidenceService = evidenceService ?? throw new ArgumentNullException(nameof(evidenceService));

    public async Task<PlanBuildResult> BuildAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Manifest, out var manifest))
        {
            return PlanBuildResult.Failed(
                "Missing required arguments: namespace and manifest.",
                KubernetesAdapterConventions.ResultReasonCodes.MissingArguments);
        }

        var policyResult = CheckManifestPolicy(manifest);
        if (policyResult.HadError)
        {
            return PlanBuildResult.Failed(
                "Manifest policy evaluation failed — the manifest could not be parsed or validated.",
                KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked);
        }

        if (policyResult.IsDenied)
        {
            var policyMessage = $"Manifest rejected by policy:{Environment.NewLine}{policyResult.FormatRefusal()}";
            return PlanBuildResult.Failed(
                policyMessage,
                KubernetesAuditHelper.DryRunFailed(
                    KubernetesAdapterConventions.AuditPhases.Request,
                    ApprovalIds.NewPlanId(),
                    KubernetesAdapterConventions.PlanOperations.Apply,
                    namespaceName,
                    Array.Empty<string>(),
                    policyMessage),
                KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked);
        }

        var applyEvidence = await evidenceService.GetApplyEvidenceAsync(namespaceName, manifest, ct).ConfigureAwait(false);
        if (applyEvidence is null)
        {
            const string message = "Evidence dry-run failed or returned an empty result.";
            return PlanBuildResult.Failed(message,
                KubernetesAuditHelper.DryRunFailed(
                    KubernetesAdapterConventions.AuditPhases.Request,
                    ApprovalIds.NewPlanId(),
                    KubernetesAdapterConventions.PlanOperations.Apply,
                    namespaceName,
                    Array.Empty<string>(),
                    message),
                KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed);
        }

        var diffs = await GetManifestDiffsAsync(namespaceName, manifest, applyEvidence.DryRun.Objects, ct)
            .ConfigureAwait(false);
        if (diffs.Error is not null)
        {
            return diffs.Error;
        }

        // Diffs guaranteed non-null when Error is null per DiffsResult contract.
        var diffValues = diffs.Diffs!;
        var objects = diffValues.Select(d => d.Object).ToArray();
        var planFindings = policyResult.Findings
            .Select(f => new KubernetesPlanPolicyFinding(f.Severity.ToString(), f.Code, f.ObjectRef, f.Message))
            .ToArray();
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Apply {objects.Length} supported Kubernetes object(s) in namespace '{namespaceName}'.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.PlanParameters.ObjectCount] = objects.Length.ToString()
            },
            objects)
        {
            Manifest = manifest,
            DryRun = applyEvidence.DryRun,
            Diffs = diffValues,
            PolicyFindings = planFindings
        };

        return BuildEnvelope(
            KubernetesAdapterConventions.PlanOperations.Apply,
            payload,
            requester,
            approvalPolicy,
            BuildFreshnessPolicy(manifestFreshnessChecks, diffValues));
    }

    private async Task<DiffsResult> GetManifestDiffsAsync(
        string namespaceName,
        string manifest,
        IEnumerable<KubernetesPlanDryRunObject> dryRunObjects,
        CancellationToken ct)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
            [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
        };

        string[] objectList = dryRunObjects.Select(obj => obj.Object).ToArray();
        var diffs = await evidenceService.GetDiffsAsync(KubernetesAdapterConventions.EvidenceTools.DiffManifest, arguments, ct)
            .ConfigureAwait(false);

        if (diffs is null)
        {
            const string message = "Diff evidence failed or returned an empty result.";
            return new DiffsResult(
                PlanBuildResult.Failed(
                    message,
                    KubernetesAuditHelper.DiffFailed(
                        null,
                        KubernetesAdapterConventions.PlanOperations.Apply,
                        namespaceName,
                        objectList,
                        message),
                    KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceEmpty),
                null);
        }

        return new DiffsResult(null, diffs);
    }

    private sealed record class DiffsResult(PlanBuildResult? Error, KubernetesPlanDiff[]? Diffs);
}
