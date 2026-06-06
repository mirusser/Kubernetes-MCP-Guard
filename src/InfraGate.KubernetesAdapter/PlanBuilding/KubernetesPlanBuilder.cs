using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter.Evidence;

namespace InfraGate.KubernetesAdapter.PlanBuilding;

public sealed class KubernetesPlanBuilder : IDomainPlanBuilder
{
    private readonly IReadOnlyDictionary<string, IOperationPlanBuilder> builders;

    internal KubernetesPlanBuilder(IKubernetesEvidenceService evidenceService)
    {
        ArgumentNullException.ThrowIfNull(evidenceService);

        builders = new Dictionary<string, IOperationPlanBuilder>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.MutationTools.ApplyManifest] = new ApplyManifestBuilder(evidenceService),
            [KubernetesAdapterConventions.MutationTools.DeleteManifest] = new DeleteManifestBuilder(evidenceService),
            [KubernetesAdapterConventions.MutationTools.ScaleDeployment] = new ScaleDeploymentBuilder(evidenceService),
            [KubernetesAdapterConventions.MutationTools.RestartDeployment] = new RestartDeploymentBuilder(evidenceService),
            [KubernetesAdapterConventions.MutationTools.SetDeploymentImage] = new SetDeploymentImageBuilder(evidenceService)
        };
    }

    public Task<PlanBuildResult> BuildAsync(
        string mutationToolName,
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        CancellationToken ct) =>
        BuildAsync(mutationToolName, arguments, requester, ApprovalPolicy.SameSubject(), ct);

    public Task<PlanBuildResult> BuildAsync(
        string mutationToolName,
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct) =>
        builders.TryGetValue(mutationToolName, out var builder)
            ? builder.BuildAsync(arguments, requester, approvalPolicy, ct)
            : Task.FromResult(PlanBuildResult.Failed(
                $"Unsupported mutation tool '{mutationToolName}'.",
                KubernetesAdapterConventions.ResultReasonCodes.UnsupportedMutationTool));

}
