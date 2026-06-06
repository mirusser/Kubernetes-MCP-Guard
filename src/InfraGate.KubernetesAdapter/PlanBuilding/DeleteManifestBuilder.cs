using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter.Evidence;
using static InfraGate.KubernetesAdapter.PlanBuilding.KubernetesBuilderInfrastructure;

namespace InfraGate.KubernetesAdapter.PlanBuilding;

internal sealed class DeleteManifestBuilder(IKubernetesEvidenceService evidenceService) : IOperationPlanBuilder
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

        var dryRunArguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
            [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
        };

        var dryRun = await evidenceService.GetDryRunAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunDeleteManifest,
            dryRunArguments,
            ct).ConfigureAwait(false);
        if (dryRun is null)
        {
            return PlanBuildResult.Failed(
                "Evidence dry-run failed or returned an empty result.",
                KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed);
        }

        var diffs = await evidenceService.GetDiffsAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffManifest,
            dryRunArguments,
            ct).ConfigureAwait(false);

        if (diffs is null)
        {
            const string message = "Diff evidence failed or returned an empty result.";
            return PlanBuildResult.Failed(
                message,
                KubernetesAuditHelper.DiffFailed(
                    null,
                    KubernetesAdapterConventions.PlanOperations.Delete,
                    namespaceName,
                    dryRun.Objects.Select(obj => obj.Object).ToArray(),
                    message),
                KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceFailed);
        }

        var objects = diffs.Select(d => d.Object).ToArray();
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Delete {objects.Length} supported Kubernetes object(s) from namespace '{namespaceName}'.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.PlanParameters.ObjectCount] = objects.Length.ToString()
            },
            objects)
        {
            Manifest = manifest,
            DryRun = dryRun,
            Diffs = diffs
        };

        return BuildEnvelope(
            KubernetesAdapterConventions.PlanOperations.Delete,
            payload,
            requester,
            approvalPolicy,
            BuildFreshnessPolicy(manifestFreshnessChecks, diffs));
    }
}
