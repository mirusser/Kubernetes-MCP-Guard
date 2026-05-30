using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.PreExecution;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalPolicyAuthorizationCheckTests
{
    [Fact]
    public async Task EvaluateAsync_SameSubjectPolicy_MatchingSubjects_ReturnsAuthorized()
    {
        var check = new ApprovalPolicyAuthorizationCheck();
        var context = new StubAuthorizationContext(
            "user-1", "user-1", ApprovalPolicy.SameSubject());

        var result = await check.EvaluateAsync(context, CancellationToken.None);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task EvaluateAsync_SameSubjectPolicy_DifferentSubjects_ReturnsDenied()
    {
        var check = new ApprovalPolicyAuthorizationCheck();
        var context = new StubAuthorizationContext(
            "requester-1", "actor-2", ApprovalPolicy.SameSubject());

        var result = await check.EvaluateAsync(context, CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_OperatorApprovalPolicy_ReturnsAuthorized()
    {
        var check = new ApprovalPolicyAuthorizationCheck();
        var context = new StubAuthorizationContext(
            "requester-1", "operator-1",
            ApprovalPolicy.OperatorApproval("infra-operators"));

        var result = await check.EvaluateAsync(context, CancellationToken.None);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task EvaluateAsync_UnknownPolicyType_ReturnsDenied()
    {
        var check = new ApprovalPolicyAuthorizationCheck();
        var context = new StubAuthorizationContext(
            "requester-1", "actor-1",
            new ApprovalPolicy("unknown-policy-type"));

        var result = await check.EvaluateAsync(context, CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Contains("unknown-policy-type", result.Reason, StringComparison.Ordinal);
    }

    private sealed class StubAuthorizationContext(
        string requesterSubject,
        string actorSubject,
        ApprovalPolicy policy) : IAuthorizationContext
    {
        public string RequesterSubject => requesterSubject;
        public string ActorSubject => actorSubject;
        public ApprovalPolicy ApprovalPolicy => policy;
        public IReadOnlySet<string> ActorGroups => new HashSet<string>(StringComparer.Ordinal);
    }
}
