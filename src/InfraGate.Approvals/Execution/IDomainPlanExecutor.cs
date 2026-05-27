using InfraGate.Approvals.Plan;
namespace InfraGate.Approvals.Execution;

public interface IDomainPlanExecutor
{
    Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct);

    Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct);
}
