namespace InfraGate.Approvals;

public interface IPlanReviewAdapter
{
    string AdapterId { get; }
    IPlanReview? TryDecodeForReview(PlanEnvelope envelope);
}
