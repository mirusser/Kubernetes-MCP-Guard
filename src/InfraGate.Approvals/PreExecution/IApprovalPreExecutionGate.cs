using InfraGate.Approvals.Execution;
namespace InfraGate.Approvals.PreExecution;

public interface IApprovalPreExecutionGate
{
    Task<PreExecutionGateResult> EvaluateAsync(
        string planId,
        IDomainPlanExecutor domainExecutor,
        CancellationToken cancellationToken);
}
