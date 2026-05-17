using InfraGate.Approvals;

namespace InfraGate.KubernetesAdapter;

public sealed record KubernetesPlan(PlanEnvelope Envelope, KubernetesPlanPayload Payload) : IPlanReview
{
    public string Id => Envelope.Id;

    public string Operation => Envelope.Operation;

    public DateTimeOffset CreatedAtUtc => Envelope.CreatedAtUtc;

    public PlanRequester Requester => Envelope.Requester;

    public string Namespace => Payload.Namespace;

    public string Description => Payload.Description;

    public Dictionary<string, string> Parameters => Payload.Parameters;

    public K8sObjectRef[] Objects => Payload.Objects;

    public string? Manifest => Payload.Manifest;

    public K8sPlanDryRun? DryRun => Payload.DryRun;

    public K8sPlanDiff[] Diffs => Payload.Diffs;

    public K8sPlanPolicyFinding[] PolicyFindings => Payload.PolicyFindings;

    bool IPlanReview.HasReviewEvidence =>
        DryRun is not null && (Diffs.Length > 0 || IsDryRunOnlyOperation(Operation));

    bool IPlanReview.CanBeApproved => !PolicyFindings.Any(f =>
        string.Equals(f.Severity, "Deny", StringComparison.Ordinal));

    private static bool IsDryRunOnlyOperation(string operation) =>
        operation is KubernetesAdapterConventions.PlanOperations.Scale
            or KubernetesAdapterConventions.PlanOperations.Restart
            or KubernetesAdapterConventions.PlanOperations.SetImage;
}
