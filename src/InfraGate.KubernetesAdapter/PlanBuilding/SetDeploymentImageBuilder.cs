using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter.Evidence;
using InfraGate.KubernetesAdapter.Policy;
using static InfraGate.KubernetesAdapter.PlanBuilding.KubernetesBuilderInfrastructure;

namespace InfraGate.KubernetesAdapter.PlanBuilding;

internal sealed class SetDeploymentImageBuilder(IKubernetesEvidenceService evidenceService) : IOperationPlanBuilder
{
    private readonly IKubernetesEvidenceService evidenceService = evidenceService ?? throw new ArgumentNullException(nameof(evidenceService));

    public async Task<PlanBuildResult> BuildAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Name, out var name) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Container, out var container) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Image, out var image))
        {
            return PlanBuildResult.Failed(
                "Missing required arguments: namespace, name, container, and image.",
                KubernetesAdapterConventions.ResultReasonCodes.MissingArguments);
        }

        var policyResult = KubernetesPolicyValidator.ValidateSetDeploymentImage(
            namespaceName,
            name,
            container,
            image,
            KubernetesPolicyOptions.Default);
        if (policyResult.IsDenied)
        {
            return PlanBuildResult.Failed(
                $"Set deployment image rejected by policy:{Environment.NewLine}{policyResult.FormatRefusal()}",
                KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked);
        }

        var dryRunArguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
            [KubernetesAdapterConventions.EvidenceArguments.Name] = name,
            [KubernetesAdapterConventions.EvidenceArguments.Container] = container,
            [KubernetesAdapterConventions.EvidenceArguments.Image] = image
        };

        var dryRun = await evidenceService.GetDryRunAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage,
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
                KubernetesAdapterConventions.PlanOperations.SetImage,
            [KubernetesAdapterConventions.EvidenceArguments.Container] = container,
            [KubernetesAdapterConventions.EvidenceArguments.Image] = image
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
                    KubernetesAdapterConventions.PlanOperations.SetImage,
                    namespaceName,
                    dryRun.Objects.Select(obj => obj.Object).ToArray(),
                    message),
                KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceFailed);
        }

        var deploymentRef = new KubernetesObjectRef(
            KubernetesAdapterConventions.ApiVersions.AppsV1,
            KubernetesAdapterConventions.ResourceKinds.Deployment,
            namespaceName,
            name);
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Update Deployment '{name}' container '{container}' image to '{image}'.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = name,
                [KubernetesAdapterConventions.PlanParameters.Container] = container,
                [KubernetesAdapterConventions.PlanParameters.Image] = image
            },
            [deploymentRef])
        {
            DryRun = dryRun,
            Diffs = diffs
        };

        return BuildEnvelope(
            KubernetesAdapterConventions.PlanOperations.SetImage,
            payload,
            requester,
            approvalPolicy,
            BuildFreshnessPolicy(deploymentFreshnessChecks, diffs));
    }
}
