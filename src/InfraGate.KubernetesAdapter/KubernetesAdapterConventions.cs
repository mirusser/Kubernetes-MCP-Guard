namespace InfraGate.KubernetesAdapter;

public static class KubernetesAdapterConventions
{
    public const string AdapterId = "kubernetes";

    public static class Canonicalizations
    {
        public const string IntentV1 = "infra-gate.kubernetes.intent.v1";
    }

    public static class ReviewRenderers
    {
        public const string PlanReviewV1 = "infra-gate.kubernetes.review.v1";
    }
}
