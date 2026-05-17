using InfraGate.Approvals;

namespace InfraGate.KubernetesAdapter;

public sealed class KubernetesPlanReviewAdapter : IPlanReviewAdapter
{
    public string AdapterId => KubernetesAdapterConventions.AdapterId;

    public IPlanReview? TryDecodeForReview(PlanEnvelope envelope)
    {
        var result = KubernetesApprovalAdapter.Decode(envelope);
        return result.Succeeded && result.Plan is not null ? result.Plan : null;
    }
}
