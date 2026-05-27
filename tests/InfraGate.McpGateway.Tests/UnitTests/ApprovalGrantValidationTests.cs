using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Grant;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalGrantValidationTests
{
    private const string Subject = "test-subject";

    [Fact]
    public void Validate_ValidEnvelopeAndGrant_ReturnsNull()
    {
        var envelope = BuildValidEnvelope();
        var grant = BuildMatchingGrant(envelope);

        var result = ApprovalGrantValidation.Validate(envelope, grant);

        Assert.Null(result);
    }

    [Fact]
    public void Validate_PlanNotYetValid_ReturnsPlanNotStartedError()
    {
        var envelope = BuildValidEnvelope(validFrom: DateTimeOffset.UtcNow.AddHours(1));
        var grant = BuildMatchingGrant(envelope);

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanNotStarted, reasonCode);
    }

    [Fact]
    public void Validate_PlanExpired_ReturnsPlanExpiredError()
    {
        var envelope = BuildValidEnvelope(validUntil: DateTimeOffset.UtcNow.AddSeconds(-1));
        var grant = BuildMatchingGrant(envelope);

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanExpired, reasonCode);
    }

    [Fact]
    public void Validate_GrantExpired_ReturnsGrantExpiredError()
    {
        var envelope = BuildValidEnvelope();
        var grant = BuildMatchingGrant(envelope, expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.GrantExpired, reasonCode);
    }

    [Fact]
    public void Validate_PlanIdMismatch_ReturnsInvalidGrantError()
    {
        var envelope = BuildValidEnvelope();
        var grant = BuildMatchingGrant(envelope) with { PlanId = "different-plan-id" };

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.InvalidGrant, reasonCode);
    }

    [Fact]
    public void Validate_RequesterSubjectMismatch_ReturnsInvalidGrantError()
    {
        var envelope = BuildValidEnvelope();
        var grant = BuildMatchingGrant(envelope) with { RequesterSubject = "different-subject" };

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.InvalidGrant, reasonCode);
    }

    [Fact]
    public void Validate_IntentDigestMismatch_ReturnsInvalidGrantError()
    {
        var envelope = BuildValidEnvelope();
        var differentDigest = ApprovalDigest.ComputeSha256("other.intent.v1", new { x = 1 });
        var grant = BuildMatchingGrant(envelope) with { IntentDigest = differentDigest };

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.InvalidGrant, reasonCode);
    }

    [Fact]
    public void Validate_ReviewDigestMismatchOnGrant_ReturnsInvalidGrantError()
    {
        var envelope = BuildValidEnvelope();
        var differentDigest = ApprovalDigest.ComputeSha256("other.review.v1", new { x = 1 });
        var grant = BuildMatchingGrant(envelope) with { ReviewDigest = differentDigest };

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.InvalidGrant, reasonCode);
    }

    [Fact]
    public void Validate_ApprovalPolicyMismatch_ReturnsInvalidGrantError()
    {
        var envelope = BuildValidEnvelope();
        var grant = BuildMatchingGrant(envelope) with { ApprovalPolicy = new ApprovalPolicy("different-policy") };

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.InvalidGrant, reasonCode);
    }

    [Fact]
    public void Validate_ExecutionReusePolicyMismatch_ReturnsInvalidGrantError()
    {
        var envelope = BuildValidEnvelope();
        var grant = BuildMatchingGrant(envelope) with { ExecutionReusePolicy = new ExecutionReusePolicy("other-policy") };

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.InvalidGrant, reasonCode);
    }

    [Fact]
    public void Validate_SameSubjectPolicyViolated_ReturnsInvalidGrantError()
    {
        var envelope = BuildValidEnvelope();
        var grant = BuildMatchingGrant(envelope) with { ApproverSubject = "different-approver" };

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.InvalidGrant, reasonCode);
    }

    [Fact]
    public void Validate_OperatorApprovalPolicyWithDifferentApprover_ReturnsNull()
    {
        var envelope = BuildValidEnvelope(
            approvalPolicy: ApprovalPolicy.OperatorApproval("kubernetes-operators"));
        var grant = BuildMatchingGrant(envelope) with { ApproverSubject = "operator-user" };

        var result = ApprovalGrantValidation.Validate(envelope, grant);

        Assert.Null(result);
    }

    [Fact]
    public void Validate_OperatorApprovalPolicyWithoutOperatorGroup_ReturnsInvalidGrantError()
    {
        var envelope = BuildValidEnvelope(
            approvalPolicy: new ApprovalPolicy(ApprovalConventions.ApprovalPolicyTypes.OperatorApproval));
        var grant = BuildMatchingGrant(envelope) with { ApproverSubject = "operator-user" };

        var (_, reasonCode) = ApprovalGrantValidation.Validate(envelope, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.InvalidGrant, reasonCode);
    }

    [Fact]
    public void Validate_ReviewDigestTamperedOnEnvelope_ReturnsDigestChangedError()
    {
        var envelope = BuildValidEnvelope();
        var tampered = envelope with { ReviewDigest = ApprovalDigest.ComputeSha256("tampered.v1", new { x = 1 }) };
        var grant = BuildMatchingGrant(tampered);

        var (_, reasonCode) = ApprovalGrantValidation.Validate(tampered, grant)!.Value;

        Assert.Equal(ApprovalConventions.ResultReasonCodes.DigestChanged, reasonCode);
    }

    [Theory]
    [InlineData("algo-1", "canon-1", "value-A", "algo-1", "canon-1", "value-A", true)]
    [InlineData("algo-2", "canon-1", "value-A", "algo-1", "canon-1", "value-A", false)]
    [InlineData("algo-1", "canon-2", "value-A", "algo-1", "canon-1", "value-A", false)]
    [InlineData("algo-1", "canon-1", "value-B", "algo-1", "canon-1", "value-A", false)]
    public void SameDigest_ComparesAllFields(
        string algo1, string canon1, string val1,
        string algo2, string canon2, string val2,
        bool expected)
    {
        var left = new ApprovalDigest(algo1, canon1, val1);
        var right = new ApprovalDigest(algo2, canon2, val2);

        Assert.Equal(expected, ApprovalGrantValidation.SameDigest(left, right));
    }

    [Theory]
    [InlineData("policy-a", "policy-a", true)]
    [InlineData("policy-a", "policy-b", false)]
    public void SamePolicy_ComparesType(string type1, string type2, bool expected)
    {
        Assert.Equal(expected, ApprovalGrantValidation.SamePolicy(
            new ApprovalPolicy(type1), new ApprovalPolicy(type2)));
    }

    [Fact]
    public void SamePolicy_ParameterMismatch_ReturnsFalse()
    {
        var left = ApprovalPolicy.OperatorApproval("kubernetes-operators");
        var right = ApprovalPolicy.OperatorApproval("platform-operators");

        Assert.False(ApprovalGrantValidation.SamePolicy(left, right));
    }

    [Theory]
    [InlineData("reuse-a", "reuse-a", true)]
    [InlineData("reuse-a", "reuse-b", false)]
    public void SameReusePolicy_ComparesType(string type1, string type2, bool expected)
    {
        Assert.Equal(expected, ApprovalGrantValidation.SameReusePolicy(
            new ExecutionReusePolicy(type1), new ExecutionReusePolicy(type2)));
    }

    private static PlanEnvelope BuildValidEnvelope(
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validUntil = null,
        ApprovalPolicy? approvalPolicy = null)
    {
        var now = DateTimeOffset.UtcNow;
        var intentDigest = ApprovalDigest.ComputeSha256("test.intent.v1", new { operation = "test-op" });

        var envelope = new PlanEnvelope(
            ApprovalIds.NewPlanId(),
            ApprovalConventions.Profiles.MutationApproval,
            "test-adapter",
            "test-op",
            now,
            validFrom ?? now.AddSeconds(-1),
            validUntil ?? now.AddHours(1),
            new PlanRequester(Subject, "test-auth"),
            approvalPolicy ?? ApprovalPolicy.SameSubject(),
            ExecutionReusePolicy.SingleExecution(),
            FreshnessPolicy.Empty,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "renderer-v1"),
            [],
            intentDigest,
            new ApprovalDigest(string.Empty, string.Empty, string.Empty),
            default);

        var reviewDigest = PlanEnvelopeFactory.ComputeReviewDigest(envelope);
        return envelope with { ReviewDigest = reviewDigest };
    }

    private static ApprovalGrant BuildMatchingGrant(
        PlanEnvelope envelope,
        DateTimeOffset? expiresAt = null)
    {
        return new ApprovalGrant(
            ApprovalIds.NewGrantId(),
            envelope.Id,
            envelope.Requester.Subject,
            envelope.Requester.Subject,
            "challenge-1",
            envelope.IntentDigest,
            envelope.ReviewDigest,
            envelope.ApprovalPolicy,
            envelope.ExecutionReusePolicy,
            DateTimeOffset.UtcNow,
            expiresAt ?? DateTimeOffset.UtcNow.AddHours(1));
    }
}
