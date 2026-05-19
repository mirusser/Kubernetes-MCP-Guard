namespace InfraGate.Approvals;

public interface IDomainAdapter : IDomainPlanBuilder, IDomainPlanExecutor, IPlanReviewAdapter, IPlanReviewRenderer
{
}
