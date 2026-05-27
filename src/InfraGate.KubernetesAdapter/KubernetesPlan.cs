using InfraGate.Approvals;
using InfraGate.Approvals.Plan;

namespace InfraGate.KubernetesAdapter;

public sealed record class KubernetesPlan(PlanEnvelope Envelope, KubernetesPlanPayload Payload) : IPlanReview
{
    public string Id => Envelope.Id;

    public string Operation => Envelope.Operation;

    public DateTimeOffset CreatedAtUtc => Envelope.CreatedAtUtc;

    public PlanRequester Requester => Envelope.Requester;

    public string Namespace => Payload.Namespace;

    public string Description => Payload.Description;

    public IReadOnlyList<PlanReviewTarget> Targets { get; } = MapTargets(Payload.Objects);

    public Dictionary<string, string> Parameters => Payload.Parameters;

    public KubernetesObjectRef[] Objects => Payload.Objects;

    public string? Manifest => Payload.Manifest;

    public KubernetesPlanDryRun? DryRun => Payload.DryRun;

    public KubernetesPlanDiff[] Diffs => Payload.Diffs;

    public KubernetesPlanPolicyFinding[] PolicyFindings => Payload.PolicyFindings;

    bool IPlanReview.HasReviewEvidence =>
        DryRun is not null && (Diffs.Length > 0 || IsDryRunOnlyOperation(Operation));

    bool IPlanReview.CanBeApproved => !PolicyFindings.Any(f =>
        string.Equals(f.Severity, KubernetesAdapterConventions.PolicySeverities.Deny, StringComparison.Ordinal));

    private static IReadOnlyList<PlanReviewTarget> MapTargets(KubernetesObjectRef[] objects) =>
        objects.Select(obj => new PlanReviewTarget(
            obj.Kind,
            obj.Name,
            obj.Namespace,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.PlanAttributeKeys.ApiVersion] = obj.ApiVersion
            })).ToArray();

    private static bool IsDryRunOnlyOperation(string operation) =>
        operation is KubernetesAdapterConventions.PlanOperations.Scale
            or KubernetesAdapterConventions.PlanOperations.Restart
            or KubernetesAdapterConventions.PlanOperations.SetImage;
}
