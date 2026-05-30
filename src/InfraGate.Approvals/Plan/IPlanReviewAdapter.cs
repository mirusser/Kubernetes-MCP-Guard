namespace InfraGate.Approvals.Plan;

public interface IPlanReviewAdapter
{
    string AdapterId { get; }
    IPlanReview? TryDecodeForReview(PlanEnvelope envelope, out string? error);
}
