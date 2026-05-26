namespace InfraGate.Approvals;

public static class PlanEnvelopeFactory
{
    public static PlanEnvelope<TPayload> Create<TPayload>( // NOSONAR:S107 — 10 params (2 optional, one generic). Parameter-object adds ceremony.
        string id,
        string adapterId,
        string operation,
        DateTimeOffset createdAtUtc,
        PlanRequester requester,
        ApprovalDigest intentDigest,
        ReviewSurfaceContext reviewSurfaceContext,
        TPayload payload,
        FreshnessPolicy? freshnessPolicy = null,
        IReadOnlyList<EvidenceArtifactSummary>? evidenceArtifacts = null,
        ApprovalPolicy? approvalPolicy = null)
    {
        var resolvedFreshnessPolicy = freshnessPolicy ?? FreshnessPolicy.Empty;
        var resolvedEvidenceArtifacts = evidenceArtifacts?.ToArray() ?? [];
        var validityWindow = new PlanValidityWindow(
            createdAtUtc,
            createdAtUtc.Add(ApprovalConventions.PlanValidity.DefaultWindow));
        var resolvedApprovalPolicy = approvalPolicy ?? ApprovalPolicy.SameSubject();
        var executionReusePolicy = ExecutionReusePolicy.SingleExecution();
        var reviewDigest = ComputeReviewDigest(
            id,
            ApprovalConventions.Profiles.MutationApproval,
            adapterId,
            operation,
            createdAtUtc,
            validityWindow,
            requester,
            resolvedApprovalPolicy,
            executionReusePolicy,
            resolvedFreshnessPolicy,
            reviewSurfaceContext,
            resolvedEvidenceArtifacts,
            intentDigest);

        return new PlanEnvelope<TPayload>(
            id,
            ApprovalConventions.Profiles.MutationApproval,
            adapterId,
            operation,
            createdAtUtc,
            validityWindow.ValidFromUtc,
            validityWindow.ValidUntilUtc,
            requester,
            resolvedApprovalPolicy,
            executionReusePolicy,
            reviewSurfaceContext,
            resolvedEvidenceArtifacts,
            intentDigest,
            reviewDigest,
            payload)
        {
            FreshnessPolicy = resolvedFreshnessPolicy
        };
    }

    public static ApprovalDigest ComputeReviewDigest(PlanEnvelope envelope) =>
        ComputeReviewDigest(
            envelope.Id,
            envelope.Profile,
            envelope.AdapterId,
            envelope.Operation,
            envelope.CreatedAtUtc,
            new PlanValidityWindow(envelope.ValidFromUtc, envelope.ValidUntilUtc),
            envelope.Requester,
            envelope.ApprovalPolicy,
            envelope.ExecutionReusePolicy,
            envelope.FreshnessPolicy,
            envelope.ReviewSurfaceContext,
            envelope.EvidenceArtifacts,
            envelope.IntentDigest);

    private static ApprovalDigest ComputeReviewDigest( // NOSONAR:S107 — Private digest: each param is a distinct canonical field. Parameter-object would add indirection.
        string id,
        string profile,
        string adapterId,
        string operation,
        DateTimeOffset createdAtUtc,
        PlanValidityWindow validityWindow,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        ExecutionReusePolicy executionReusePolicy,
        FreshnessPolicy freshnessPolicy,
        ReviewSurfaceContext reviewSurfaceContext,
        IReadOnlyList<EvidenceArtifactSummary> evidenceArtifacts,
        ApprovalDigest intentDigest)
    {
        return ApprovalDigest.ComputeSha256(
            ApprovalConventions.Canonicalizations.ProfileReviewV1,
            new
            {
                id,
                profile,
                adapterId,
                operation,
                createdAtUtc,
                validFromUtc = validityWindow.ValidFromUtc,
                validUntilUtc = validityWindow.ValidUntilUtc,
                requester,
                approvalPolicy,
                executionReusePolicy,
                freshnessPolicy,
                reviewSurfaceContext,
                evidenceArtifacts,
                intentDigest
            });
    }
}
