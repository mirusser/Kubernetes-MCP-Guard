using InfraGate.Approvals;
using InfraGate.Approvals.Plan;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalPolicyTests
{
    [Fact]
    public void SameSubject_HasCorrectType()
    {
        var policy = ApprovalPolicy.SameSubject();

        Assert.Equal(ApprovalConventions.ApprovalPolicyTypes.SameSubject, policy.Type);
        Assert.Null(policy.Parameters);
    }

    [Fact]
    public void OperatorApproval_HasCorrectTypeAndGroup()
    {
        var policy = ApprovalPolicy.OperatorApproval("infra-operators");

        Assert.Equal(ApprovalConventions.ApprovalPolicyTypes.OperatorApproval, policy.Type);
        Assert.NotNull(policy.Parameters);
        Assert.Equal("infra-operators", policy.Parameters[ApprovalConventions.ApprovalPolicyParameters.OperatorGroup]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OperatorApproval_EmptyOrWhitespaceGroup_ThrowsArgumentException(string group)
    {
        Assert.Throws<ArgumentException>(() => ApprovalPolicy.OperatorApproval(group));
    }

    [Fact]
    public void DefaultConstructor_HasEmptyTypeAndNullParameters()
    {
        var policy = new ApprovalPolicy();

        Assert.Equal(string.Empty, policy.Type);
        Assert.Null(policy.Parameters);
    }

    [Fact]
    public void Constructor_WithNullParameters_StoresNullParameters()
    {
        var policy = new ApprovalPolicy("some-type", null);

        Assert.Null(policy.Parameters);
    }

    [Fact]
    public void Constructor_WithEmptyParameters_NormalizesToNull()
    {
        var policy = new ApprovalPolicy("some-type", new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Null(policy.Parameters);
    }

    [Fact]
    public void Equals_SameSubjectPolicies_ReturnsTrue()
    {
        var left = ApprovalPolicy.SameSubject();
        var right = ApprovalPolicy.SameSubject();

        Assert.True(left.Equals(right));
    }

    [Fact]
    public void Equals_SameOperatorGroup_ReturnsTrue()
    {
        var left = ApprovalPolicy.OperatorApproval("infra-operators");
        var right = ApprovalPolicy.OperatorApproval("infra-operators");

        Assert.True(left.Equals(right));
    }

    [Fact]
    public void Equals_DifferentTypes_ReturnsFalse()
    {
        var left = ApprovalPolicy.SameSubject();
        var right = ApprovalPolicy.OperatorApproval("infra-operators");

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_DifferentOperatorGroups_ReturnsFalse()
    {
        var left = ApprovalPolicy.OperatorApproval("infra-operators");
        var right = ApprovalPolicy.OperatorApproval("platform-operators");

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_NullOther_ReturnsFalse()
    {
        var policy = ApprovalPolicy.SameSubject();

        Assert.False(policy.Equals(null));
    }

    [Fact]
    public void Equals_BothHaveNoParameters_ReturnsTrue()
    {
        var left = new ApprovalPolicy("type-a");
        var right = new ApprovalPolicy("type-a");

        Assert.True(left.Equals(right));
    }

    [Fact]
    public void Equals_OneHasParameters_OtherDoesNot_ReturnsFalse()
    {
        var left = new ApprovalPolicy("type-a");
        var right = new ApprovalPolicy("type-a",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = "val" });

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void GetHashCode_SameSubjectTwice_ReturnsSameHash()
    {
        var left = ApprovalPolicy.SameSubject();
        var right = ApprovalPolicy.SameSubject();

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_SameOperatorGroup_ReturnsSameHash()
    {
        var left = ApprovalPolicy.OperatorApproval("infra-operators");
        var right = ApprovalPolicy.OperatorApproval("infra-operators");

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentTypes_ReturnsDifferentHashes()
    {
        var left = ApprovalPolicy.SameSubject();
        var right = ApprovalPolicy.OperatorApproval("infra-operators");

        Assert.NotEqual(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Parameters_AreSortedByKey()
    {
        var policy = new ApprovalPolicy(
            "test",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["z-key"] = "z-value",
                ["a-key"] = "a-value",
            });

        var keys = policy.Parameters!.Keys.ToList();
        Assert.Equal("a-key", keys[0]);
        Assert.Equal("z-key", keys[1]);
    }
}
