using InfraGate.Approvals.Plan;

namespace InfraGate.KubernetesAdapter.PlanBuilding;

internal interface IOperationPlanBuilder
{
    Task<PlanBuildResult> BuildAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct);
}
