using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
namespace InfraGate.Approvals.Grant;

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

        var policyValidation = ValidatePolicy(envelope.ApprovalPolicy, grant);
        if (policyValidation is not null)
        {
            return policyValidation.Value;
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
        string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
        SameParameters(left.Parameters, right.Parameters);

    public static bool SameReusePolicy(ExecutionReusePolicy left, ExecutionReusePolicy right) =>
        string.Equals(left.Type, right.Type, StringComparison.Ordinal);

    private static (string Message, string ReasonCode)? ValidatePolicy(ApprovalPolicy policy, ApprovalGrant grant)
    {
        return policy.Type switch
        {
            ApprovalConventions.ApprovalPolicyTypes.SameSubject
                when !string.Equals(grant.RequesterSubject, grant.ApproverSubject, StringComparison.Ordinal) =>
                    ($"Approval grant '{grant.Id}' violates same-subject approval policy.",
                        ApprovalConventions.ResultReasonCodes.InvalidGrant),
            ApprovalConventions.ApprovalPolicyTypes.OperatorApproval
                when !TryGetOperatorGroup(policy, out _) =>
                    ($"Approval grant '{grant.Id}' is missing operator approval policy parameters.",
                        ApprovalConventions.ResultReasonCodes.InvalidGrant),
            ApprovalConventions.ApprovalPolicyTypes.SameSubject or
                ApprovalConventions.ApprovalPolicyTypes.OperatorApproval => null,
            _ => ($"Approval grant '{grant.Id}' uses unsupported approval policy '{policy.Type}'.",
                ApprovalConventions.ResultReasonCodes.InvalidGrant)
        };
    }

    private static bool TryGetOperatorGroup(ApprovalPolicy policy, out string operatorGroup)
    {
        if (policy.Parameters is not null &&
            policy.Parameters.TryGetValue(ApprovalConventions.ApprovalPolicyParameters.OperatorGroup, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            operatorGroup = value;
            return true;
        }

        operatorGroup = string.Empty;
        return false;
    }

    private static bool SameParameters(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (left is null || left.Count == 0)
        {
            return right is null || right.Count == 0;
        }

        if (right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, leftValue) in left)
        {
            if (!right.TryGetValue(key, out var rightValue) ||
                !string.Equals(leftValue, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
