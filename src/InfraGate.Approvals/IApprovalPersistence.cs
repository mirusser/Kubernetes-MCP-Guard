namespace InfraGate.Approvals;

public interface IApprovalPersistence :
    IApprovalPlanWorkflow,
    IApprovalChallengeWorkflow,
    IApprovalExecutionWorkflow,
    IApprovalAuditPublisher
{
}
