namespace InfraGate.Approvals;

public interface IApprovalPreExecutionGate
{
    Task<PreExecutionGateResult> EvaluateAsync(
        string planId,
        IDomainPlanExecutor domainExecutor,
        CancellationToken cancellationToken);
}
