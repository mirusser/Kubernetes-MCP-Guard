namespace InfraGate.Approvals;

public interface IDomainPlanExecutor
{
    Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct);
}
