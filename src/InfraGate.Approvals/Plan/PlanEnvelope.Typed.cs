namespace InfraGate.Approvals.Plan;

public sealed record class PlanEnvelope<TPayload>(
    string Id,
    string Profile,
    string AdapterId,
    string Operation,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    PlanRequester Requester,
    ApprovalPolicy ApprovalPolicy,
    ExecutionReusePolicy ExecutionReusePolicy,
    ReviewSurfaceContext ReviewSurfaceContext,
    EvidenceArtifactSummary[] EvidenceArtifacts,
    ApprovalDigest IntentDigest,
    ApprovalDigest ReviewDigest,
    TPayload Payload)
{
    public FreshnessPolicy FreshnessPolicy { get; init; } = FreshnessPolicy.Empty;
}
