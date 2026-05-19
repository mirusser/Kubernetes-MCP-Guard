namespace InfraGate.Approvals;

public interface IPlanReview
{
    PlanEnvelope Envelope { get; }
    bool HasReviewEvidence { get; }
    bool CanBeApproved { get; }
}
