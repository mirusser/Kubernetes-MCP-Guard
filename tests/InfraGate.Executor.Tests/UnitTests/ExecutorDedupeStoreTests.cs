using InfraGate.Executor.Watch;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorDedupeStoreTests
{
    [Fact]
    public void TryTrack_NewPlanId_ReturnsTrue()
    {
        var store = new ExecutorDedupeStore();

        Assert.True(store.TryTrack("plan-1"));
    }

    [Fact]
    public void TryTrack_SamePlanIdTwice_SecondReturnsFalse()
    {
        var store = new ExecutorDedupeStore();
        store.TryTrack("plan-1");

        Assert.False(store.TryTrack("plan-1"));
    }

    [Fact]
    public void TryTrack_DistinctIds_BothReturnTrue()
    {
        var store = new ExecutorDedupeStore();

        Assert.True(store.TryTrack("plan-a"));
        Assert.True(store.TryTrack("plan-b"));
    }

    [Fact]
    public void Remove_TrackedPlanId_AllowsRetrack()
    {
        var store = new ExecutorDedupeStore();
        store.TryTrack("plan-1");

        store.Remove("plan-1");

        Assert.True(store.TryTrack("plan-1"));
    }

    [Fact]
    public void Remove_UnknownPlanId_DoesNotThrow()
    {
        var store = new ExecutorDedupeStore();

        store.Remove("plan-unknown");
    }
}
