namespace InfraGate.Approvals;

public static class PlanEnvelopeFactory
{
    public static PlanEnvelope<TPayload> Create<TPayload>(
        string id,
        string adapterId,
        string operation,
        DateTimeOffset createdAtUtc,
        PlanRequester requester,
        ApprovalDigest intentDigest,
        ReviewSurfaceContext reviewSurfaceContext,
        TPayload payload)
    {
        var validityWindow = new PlanValidityWindow(
            createdAtUtc,
            createdAtUtc.Add(ApprovalConventions.PlanValidity.DefaultWindow));
        var approvalPolicy = ApprovalPolicy.SameSubject();
        var executionReusePolicy = ExecutionReusePolicy.SingleExecution();
        var reviewDigest = ComputeReviewDigest(
            id,
            ApprovalConventions.Profiles.MutationApproval,
            adapterId,
            operation,
            createdAtUtc,
            validityWindow,
            requester,
            approvalPolicy,
            executionReusePolicy,
            reviewSurfaceContext,
            intentDigest,
            payload);

        return new PlanEnvelope<TPayload>(
            id,
            ApprovalConventions.Profiles.MutationApproval,
            adapterId,
            operation,
            createdAtUtc,
            validityWindow.ValidFromUtc,
            validityWindow.ValidUntilUtc,
            requester,
            approvalPolicy,
            executionReusePolicy,
            reviewSurfaceContext,
            intentDigest,
            reviewDigest,
            payload);
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
            envelope.ReviewSurfaceContext,
            envelope.IntentDigest,
            envelope.Payload);

    private static ApprovalDigest ComputeReviewDigest<TPayload>(
        string id,
        string profile,
        string adapterId,
        string operation,
        DateTimeOffset createdAtUtc,
        PlanValidityWindow validityWindow,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        ExecutionReusePolicy executionReusePolicy,
        ReviewSurfaceContext reviewSurfaceContext,
        ApprovalDigest intentDigest,
        TPayload payload)
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
                reviewSurfaceContext,
                intentDigest,
                payload
            });
    }
}
