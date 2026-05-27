using InfraGate.Approvals;
using InfraGate.Approvals.Plan;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class FixedTimeStringComparerTests
{
    [Fact]
    public void Equals_ReturnsTrue_ForIdenticalStrings()
        => Assert.True(FixedTimeStringComparer.Equals("abc123", "abc123"));

    [Fact]
    public void Equals_ReturnsFalse_ForDifferentStrings()
        => Assert.False(FixedTimeStringComparer.Equals("abc123", "abc124"));

    [Fact]
    public void Equals_ReturnsFalse_ForDifferentLengthStrings()
        => Assert.False(FixedTimeStringComparer.Equals("abc123", "abc1234"));
}
