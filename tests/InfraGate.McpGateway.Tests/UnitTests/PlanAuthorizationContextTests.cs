using InfraGate.Approvals;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class PlanAuthorizationContextTests
{
    [Fact]
    public void Constructor_WithRequiredArgsOnly_DefaultsApprovalPolicyToSameSubject()
    {
        var context = new PlanAuthorizationContext("requester", "actor");

        Assert.Equal(ApprovalConventions.ApprovalPolicyTypes.SameSubject, context.ApprovalPolicy.Type);
    }

    [Fact]
    public void Constructor_WithRequiredArgsOnly_DefaultsActorGroupsToEmpty()
    {
        var context = new PlanAuthorizationContext("requester", "actor");

        Assert.Empty(context.ActorGroups);
    }

    [Fact]
    public void Constructor_WithExplicitPolicy_UsesProvidedPolicy()
    {
        var policy = ApprovalPolicy.OperatorApproval("infra-operators");
        var context = new PlanAuthorizationContext("requester", "actor", Policy: policy);

        Assert.Equal(ApprovalConventions.ApprovalPolicyTypes.OperatorApproval, context.ApprovalPolicy.Type);
    }

    [Fact]
    public void Constructor_WithNullPolicy_DefaultsToSameSubject()
    {
        var context = new PlanAuthorizationContext("requester", "actor", Policy: null);

        Assert.Equal(ApprovalConventions.ApprovalPolicyTypes.SameSubject, context.ApprovalPolicy.Type);
    }

    [Fact]
    public void Constructor_WithGroups_SetsActorGroups()
    {
        var groups = new HashSet<string>(StringComparer.Ordinal) { "infra-operators", "platform-ops" };
        var context = new PlanAuthorizationContext("requester", "actor", Groups: groups);

        Assert.Contains("infra-operators", context.ActorGroups);
        Assert.Contains("platform-ops", context.ActorGroups);
    }

    [Fact]
    public void Constructor_WithNullGroups_DefaultsToEmptySet()
    {
        var context = new PlanAuthorizationContext("requester", "actor", Groups: null);

        Assert.NotNull(context.ActorGroups);
        Assert.Empty(context.ActorGroups);
    }

    [Fact]
    public void RequesterSubject_MatchesConstructorArg()
    {
        var context = new PlanAuthorizationContext("user-123", "actor");

        Assert.Equal("user-123", context.RequesterSubject);
    }

    [Fact]
    public void ActorSubject_MatchesConstructorArg()
    {
        var context = new PlanAuthorizationContext("requester", "actor-456");

        Assert.Equal("actor-456", context.ActorSubject);
    }
}
