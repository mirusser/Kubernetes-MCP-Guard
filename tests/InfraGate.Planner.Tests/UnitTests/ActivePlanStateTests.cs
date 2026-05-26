using InfraGate.Planner.Dedupe;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class ActivePlanStateTests
{
    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(1);

        var state = new ActivePlanState("anomaly-1", "plan-1", now, expires);

        Assert.Equal("anomaly-1", state.AnomalyId);
        Assert.Equal("plan-1", state.PlanId);
        Assert.Equal(now, state.ProposedAt);
        Assert.Equal(expires, state.ExpiresAt);
        Assert.Equal(now, state.LastAccessedAt);
    }

    [Fact]
    public void IsExpired_BeforeExpiry_ReturnsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ActivePlanState("anomaly-1", "plan-1", now, now.AddHours(1));

        Assert.False(state.IsExpired);
    }

    [Fact]
    public void IsExpired_AfterExpiry_ReturnsTrue()
    {
        var past = DateTimeOffset.UtcNow.AddSeconds(-1);
        var state = new ActivePlanState("anomaly-1", "plan-1", past.AddHours(-1), past);

        Assert.True(state.IsExpired);
    }

    [Fact]
    public void LastAccessedAt_CanBeUpdated()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ActivePlanState("anomaly-1", "plan-1", now, now.AddHours(1));
        var later = now.AddMinutes(5);

        state.LastAccessedAt = later;

        Assert.Equal(later, state.LastAccessedAt);
    }
}
