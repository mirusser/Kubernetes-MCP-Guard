namespace InfraGate.Approvals;

public static class ApprovalGrantValidation
{
    public static (string Message, string ReasonCode)? Validate(PlanEnvelope envelope, ApprovalGrant grant)
    {
        var now = DateTimeOffset.UtcNow;
        if (envelope.ValidFromUtc > now)
        {
            return ($"Plan '{envelope.Id}' is not valid yet.",
                ApprovalConventions.ResultReasonCodes.PlanNotStarted);
        }

        if (envelope.ValidUntilUtc <= now)
        {
            return ($"Plan '{envelope.Id}' expired before execution.",
                ApprovalConventions.ResultReasonCodes.PlanExpired);
        }

        if (grant.ExpiresAtUtc <= now)
        {
            return ($"Approval grant '{grant.Id}' expired before execution.",
                ApprovalConventions.ResultReasonCodes.GrantExpired);
        }

        if (!string.Equals(grant.PlanId, envelope.Id, StringComparison.Ordinal) ||
            !string.Equals(grant.RequesterSubject, envelope.Requester.Subject, StringComparison.Ordinal) ||
            !SameDigest(grant.IntentDigest, envelope.IntentDigest) ||
            !SameDigest(grant.ReviewDigest, envelope.ReviewDigest) ||
            !SamePolicy(grant.ApprovalPolicy, envelope.ApprovalPolicy) ||
            !SameReusePolicy(grant.ExecutionReusePolicy, envelope.ExecutionReusePolicy))
        {
            return ($"Approval grant '{grant.Id}' no longer matches plan '{envelope.Id}'.",
                ApprovalConventions.ResultReasonCodes.InvalidGrant);
        }

        if (string.Equals(envelope.ApprovalPolicy.Type, ApprovalConventions.ApprovalPolicyTypes.SameSubject, StringComparison.Ordinal) &&
            !string.Equals(grant.RequesterSubject, grant.ApproverSubject, StringComparison.Ordinal))
        {
            return ($"Approval grant '{grant.Id}' violates same-subject approval policy.",
                ApprovalConventions.ResultReasonCodes.InvalidGrant);
        }

        var actualReviewDigest = PlanEnvelopeFactory.ComputeReviewDigest(envelope);
        if (!SameDigest(envelope.ReviewDigest, actualReviewDigest))
        {
            return ($"Plan '{envelope.Id}' review digest no longer matches the pending plan.",
                ApprovalConventions.ResultReasonCodes.DigestChanged);
        }

        return null;
    }

    public static bool SameDigest(ApprovalDigest left, ApprovalDigest right) =>
        string.Equals(left.Algorithm, right.Algorithm, StringComparison.Ordinal) &&
        string.Equals(left.Canonicalization, right.Canonicalization, StringComparison.Ordinal) &&
        FixedTimeStringComparer.Equals(left.Value, right.Value);

    public static bool SamePolicy(ApprovalPolicy left, ApprovalPolicy right) =>
        string.Equals(left.Type, right.Type, StringComparison.Ordinal);

    public static bool SameReusePolicy(ExecutionReusePolicy left, ExecutionReusePolicy right) =>
        string.Equals(left.Type, right.Type, StringComparison.Ordinal);
}
