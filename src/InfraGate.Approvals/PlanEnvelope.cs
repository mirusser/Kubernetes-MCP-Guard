using System.Text.Json;

namespace InfraGate.Approvals;

public sealed record PlanEnvelope
{
    public PlanEnvelope() { }

    public PlanEnvelope(
        string id,
        string profile,
        string adapterId,
        string operation,
        DateTimeOffset createdAtUtc,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        ExecutionReusePolicy executionReusePolicy,
        FreshnessPolicy freshnessPolicy,
        ReviewSurfaceContext reviewSurfaceContext,
        EvidenceArtifactSummary[] evidenceArtifacts,
        ApprovalDigest intentDigest,
        ApprovalDigest reviewDigest,
        JsonElement payload)
    {
        Id = id;
        Profile = profile;
        AdapterId = adapterId;
        Operation = operation;
        CreatedAtUtc = createdAtUtc;
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
        Requester = requester;
        ApprovalPolicy = approvalPolicy;
        ExecutionReusePolicy = executionReusePolicy;
        FreshnessPolicy = freshnessPolicy;
        ReviewSurfaceContext = reviewSurfaceContext;
        EvidenceArtifacts = evidenceArtifacts;
        IntentDigest = intentDigest;
        ReviewDigest = reviewDigest;
        Payload = payload;
    }

    public string Id { get; init; } = string.Empty;

    public string Profile { get; init; } = string.Empty;

    public string AdapterId { get; init; } = string.Empty;

    public string Operation { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ValidFromUtc { get; init; }

    public DateTimeOffset ValidUntilUtc { get; init; }

    public PlanRequester Requester { get; init; } = new(string.Empty, null);

    public ApprovalPolicy ApprovalPolicy { get; init; } = new();

    public ExecutionReusePolicy ExecutionReusePolicy { get; init; } = new();

    public FreshnessPolicy FreshnessPolicy { get; init; } = FreshnessPolicy.Empty;

    public ReviewSurfaceContext ReviewSurfaceContext { get; init; } = new(string.Empty, string.Empty);

    public EvidenceArtifactSummary[] EvidenceArtifacts { get; init; } = [];

    public ApprovalDigest IntentDigest { get; init; } = new(string.Empty, string.Empty, string.Empty);

    public ApprovalDigest ReviewDigest { get; init; } = new(string.Empty, string.Empty, string.Empty);

    public JsonElement Payload { get; init; }
}
