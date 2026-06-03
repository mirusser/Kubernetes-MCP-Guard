using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter.Evidence;
using static InfraGate.KubernetesAdapter.PlanBuilding.KubernetesBuilderInfrastructure;

namespace InfraGate.KubernetesAdapter.PlanBuilding;

internal sealed class RestartDeploymentBuilder(IKubernetesEvidenceService evidenceService) : IOperationPlanBuilder
{
    private readonly IKubernetesEvidenceService evidenceService = evidenceService ?? throw new ArgumentNullException(nameof(evidenceService));

    public async Task<PlanBuildResult> BuildAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Name, out var name))
        {
            return PlanBuildResult.Failed(
                "Missing required arguments: namespace and name.",
                KubernetesAdapterConventions.ResultReasonCodes.MissingArguments);
        }

        var dryRunArguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
            [KubernetesAdapterConventions.EvidenceArguments.Name] = name
        };

        var dryRun = await evidenceService.GetDryRunAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunRestartDeployment,
            dryRunArguments,
            ct).ConfigureAwait(false);
        if (dryRun is null)
        {
            return PlanBuildResult.Failed(
                "Evidence dry-run failed or returned an empty result.",
                KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed);
        }

        var diffArguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
            [KubernetesAdapterConventions.EvidenceArguments.Name] = name,
            [KubernetesAdapterConventions.EvidenceArguments.Operation] =
                KubernetesAdapterConventions.PlanOperations.Restart
        };

        var diffs = await evidenceService.GetDiffsAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffDeployment,
            diffArguments,
            ct).ConfigureAwait(false);
        if (diffs is null)
        {
            const string message = "Diff evidence failed or returned an empty result.";
            return PlanBuildResult.Failed(
                message,
                KubernetesAuditHelper.DiffFailed(
                    null,
                    KubernetesAdapterConventions.PlanOperations.Restart,
                    namespaceName,
                    dryRun.Objects.Select(obj => obj.Object).ToArray(),
                    message),
                KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceFailed);
        }

        var restartedAtUtc = DateTimeOffset.UtcNow.ToString(ApprovalConventions.DateTimeFormats.RoundTrip);
        var deploymentRef = new KubernetesObjectRef(
            KubernetesAdapterConventions.ApiVersions.AppsV1,
            KubernetesAdapterConventions.ResourceKinds.Deployment,
            namespaceName,
            name);
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Restart Deployment '{name}' in namespace '{namespaceName}'.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = name,
                [KubernetesAdapterConventions.PlanParameters.RestartedAtUtc] = restartedAtUtc
            },
            [deploymentRef])
        {
            DryRun = dryRun,
            Diffs = diffs
        };

        return BuildEnvelope(
            KubernetesAdapterConventions.PlanOperations.Restart,
            payload,
            requester,
            approvalPolicy,
            new FreshnessPolicy(deploymentFreshnessChecks));
    }
}
