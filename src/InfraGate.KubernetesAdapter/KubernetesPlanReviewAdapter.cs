using InfraGate.Approvals;

namespace InfraGate.KubernetesAdapter;

public sealed class KubernetesPlanReviewAdapter : IPlanReviewAdapter
{
    public string AdapterId => KubernetesAdapterConventions.AdapterId;

    public IPlanReview? TryDecodeForReview(PlanEnvelope envelope, out string? error)
    {
        var result = KubernetesApprovalAdapter.Decode(envelope);
        if (result.Succeeded && result.Plan is not null)
        {
            error = null;
            return result.Plan;
        }

        error = result.Message;
        return null;
    }
}
