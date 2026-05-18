namespace InfraGate.Approvals;

public sealed record PlanEnvelope<TPayload>(
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
