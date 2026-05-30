namespace InfraGate.Approvals.Plan;

public interface IPlanReview
{
    PlanEnvelope Envelope { get; }
    string Description { get; }
    IReadOnlyList<PlanReviewTarget> Targets { get; }
    bool HasReviewEvidence { get; }
    bool CanBeApproved { get; }
}
