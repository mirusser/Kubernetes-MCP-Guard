using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Challenge;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Audit;
namespace InfraGate.Approvals;

public interface IApprovalPersistence :
    IApprovalPlanWorkflow,
    IApprovalChallengeWorkflow,
    IApprovalExecutionWorkflow,
    IApprovalAuditPublisher
{
}
