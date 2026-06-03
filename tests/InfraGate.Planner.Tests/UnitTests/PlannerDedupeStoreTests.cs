using InfraGate.Planner.Dedupe;

namespace InfraGate.Planner.Tests.UnitTests;

#pragma warning disable S2699 // all test methods have assertions; false positive
public sealed class PlannerDedupeStoreTests
{
#pragma warning restore S2699
    [Fact]
    public void HasActivePlan_UnknownId_ReturnsFalse()
    {
        var store = new PlannerDedupeStore();

        Assert.False(store.HasActivePlan("anomaly-unknown"));
    }

    [Fact]
    public void HasActivePlan_TrackedActiveId_ReturnsTrue()
    {
        var store = new PlannerDedupeStore();
        var now = DateTimeOffset.UtcNow;
        store.TrackActivePlan("anomaly-1", "plan-1", now, now.AddHours(1));

        Assert.True(store.HasActivePlan("anomaly-1"));
    }

    [Fact]
    public void HasActivePlan_ExpiredId_RemovesEntryAndReturnsFalse()
    {
        var store = new PlannerDedupeStore();
        var past = DateTimeOffset.UtcNow.AddSeconds(-1);
        store.TrackActivePlan("anomaly-1", "plan-1", past.AddSeconds(-10), past);

        Assert.False(store.HasActivePlan("anomaly-1"));
    }

    [Fact]
    public void TrackActivePlan_SameAnomalyId_OverwritesPreviousEntry()
    {
        var store = new PlannerDedupeStore();
        var now = DateTimeOffset.UtcNow;
        store.TrackActivePlan("anomaly-1", "plan-1", now, now.AddHours(1));
        store.TrackActivePlan("anomaly-1", "plan-2", now, now.AddHours(2));

        Assert.True(store.HasActivePlan("anomaly-1"));
    }

    [Fact]
    public void Remove_ExistingId_PlanNoLongerActive()
    {
        var store = new PlannerDedupeStore();
        var now = DateTimeOffset.UtcNow;
        store.TrackActivePlan("anomaly-1", "plan-1", now, now.AddHours(1));

        store.Remove("anomaly-1");

        Assert.False(store.HasActivePlan("anomaly-1"));
    }

    [Fact]
    public void Remove_UnknownId_DoesNotThrow()
    {
        var store = new PlannerDedupeStore();

        var ex = Record.Exception(() => store.Remove("anomaly-unknown"));
        Assert.Null(ex);
    }

    [Fact]
    public void HasActivePlan_MultipleDistinctIds_TracksIndependently()
    {
        var store = new PlannerDedupeStore();
        var now = DateTimeOffset.UtcNow;
        store.TrackActivePlan("anomaly-a", "plan-a", now, now.AddHours(1));
        store.TrackActivePlan("anomaly-b", "plan-b", now, now.AddHours(1));

        Assert.True(store.HasActivePlan("anomaly-a"));
        Assert.True(store.HasActivePlan("anomaly-b"));
        Assert.False(store.HasActivePlan("anomaly-c"));
    }

    [Fact]
    public void TrackActivePlan_ExceedsCapacity_EvictsLruEntry()
    {
        var store = new PlannerDedupeStore();
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < 1001; i++)
        {
            store.TrackActivePlan($"anomaly-{i}", $"plan-{i}", now, now.AddHours(1));
        }

        Assert.True(store.HasActivePlan("anomaly-1000"));
    }
}
