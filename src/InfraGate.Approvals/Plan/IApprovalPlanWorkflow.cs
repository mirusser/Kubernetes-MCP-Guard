using InfraGate.Approvals.Grant;
namespace InfraGate.Approvals.Plan;

public interface IApprovalPlanWorkflow
{
    Task<ApprovalPlanResult> CreatePlanAsync(
        PlanEnvelope envelope,
        string targetNamespace,
        CancellationToken cancellationToken);

    Task<PendingPlanResult> GetPendingPlanAsync(
        string planId,
        CancellationToken cancellationToken);

    Task<GrantedPlanResult> GetGrantedPlanAsync(
        string planId,
        CancellationToken cancellationToken);

    Task<PlanStatusResult> GetPlanStatusAsync(
        string planId,
        CancellationToken cancellationToken);
}
