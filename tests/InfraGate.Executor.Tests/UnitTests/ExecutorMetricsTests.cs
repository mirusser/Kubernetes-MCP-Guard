using System.Diagnostics.Metrics;
using InfraGate.Executor.Diagnostics;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorMetricsTests
{
    [Fact]
    public void Meter_HasExpectedNameAndVersion()
    {
        Assert.Equal(ExecutorMetrics.MeterName, ExecutorMetrics.Meter.Name);
        Assert.Equal(ExecutorMetrics.MeterVersion, ExecutorMetrics.Meter.Version);
    }

    [Fact]
    public void CreateWatchTimeoutCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-executor-metrics");
        var counter = ExecutorMetrics.CreateWatchTimeoutCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateWatchFailedCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-executor-metrics");
        var counter = ExecutorMetrics.CreateWatchFailedCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateExecuteSucceededCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-executor-metrics");
        var counter = ExecutorMetrics.CreateExecuteSucceededCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateExecuteFailedCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-executor-metrics");
        var counter = ExecutorMetrics.CreateExecuteFailedCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateExecuteBlockedCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-executor-metrics");
        var counter = ExecutorMetrics.CreateExecuteBlockedCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CounterNames_UseExpectedPrefix()
    {
        Assert.StartsWith("infragate.executor.", ExecutorMetrics.WatchTimeoutCounterName);
        Assert.StartsWith("infragate.executor.", ExecutorMetrics.ExecuteSucceededCounterName);
        Assert.StartsWith("infragate.executor.", ExecutorMetrics.ExecuteFailedCounterName);
    }
}
