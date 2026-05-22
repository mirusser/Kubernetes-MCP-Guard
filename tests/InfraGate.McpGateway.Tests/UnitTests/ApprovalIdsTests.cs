using InfraGate.Approvals;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalIdsTests
{
    [Theory]
    [MemberData(nameof(AllIdGenerators))]
    public void IdGenerator_ReturnsUppercaseHexString(Func<string> generator)
    {
        var id = generator();

        Assert.Matches("^[0-9A-F]+$", id);
    }

    [Theory]
    [MemberData(nameof(AllIdGenerators))]
    public void IdGenerator_CalledTwice_ProducesDistinctValues(Func<string> generator)
    {
        Assert.NotEqual(generator(), generator());
    }

    [Fact]
    public void NewChallengeId_IsLongerThanPlanId()
    {
        // ChallengeId uses 32 random bytes (64 hex chars); others use 16 bytes (32 hex chars)
        Assert.True(ApprovalIds.NewChallengeId().Length > ApprovalIds.NewPlanId().Length);
    }

    [Fact]
    public void NewPlanId_HasExpectedLength()
    {
        Assert.Equal(32, ApprovalIds.NewPlanId().Length);
    }

    [Fact]
    public void NewChallengeId_HasExpectedLength()
    {
        Assert.Equal(64, ApprovalIds.NewChallengeId().Length);
    }

    public static TheoryData<Func<string>> AllIdGenerators() => new()
    {
        ApprovalIds.NewPlanId,
        ApprovalIds.NewChallengeId,
        ApprovalIds.NewGrantId,
        ApprovalIds.NewChallengeOutcomeId,
        ApprovalIds.NewExecutionAttemptId,
        ApprovalIds.NewExecutionOutcomeId,
    };
}
