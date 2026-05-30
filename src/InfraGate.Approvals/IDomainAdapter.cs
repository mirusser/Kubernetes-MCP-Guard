using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Execution;
namespace InfraGate.Approvals;

public interface IDomainAdapter : IDomainPlanBuilder, IDomainPlanExecutor, IPlanReviewAdapter
{
}
