using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Challenge;
using InfraGate.Approvals.Execution;
namespace InfraGate.Approvals;

public interface IApprovalPersistence :
    IApprovalPlanWorkflow,
    IApprovalChallengeWorkflow,
    IApprovalExecutionWorkflow
{
}
