using System.Diagnostics.Metrics;
using InfraGate.Planner.Diagnostics;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerMetricsTests
{
    [Fact]
    public void Meter_HasExpectedNameAndVersion()
    {
        Assert.Equal(PlannerMetrics.MeterName, PlannerMetrics.Meter.Name);
        Assert.Equal(PlannerMetrics.MeterVersion, PlannerMetrics.Meter.Version);
    }

    [Fact]
    public void CreateDecisionTimeoutCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-planner-metrics");
        var counter = PlannerMetrics.CreateDecisionTimeoutCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateProposeFailedCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-planner-metrics");
        var counter = PlannerMetrics.CreateProposeFailedCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateHandoffSinkFailedCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-planner-metrics");
        var counter = PlannerMetrics.CreateHandoffSinkFailedCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CounterNames_UseExpectedPrefix()
    {
        Assert.StartsWith("infragate.planner.", PlannerMetrics.HandoffSinkFailedCounterName);
    }

    [Fact]
    public void CreateDecisionTimeoutCounter_NullMeter_UsesDefaultMeter()
    {
        var counter = PlannerMetrics.CreateDecisionTimeoutCounter(null);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateProposeFailedCounter_NullMeter_UsesDefaultMeter()
    {
        var counter = PlannerMetrics.CreateProposeFailedCounter(null);
        Assert.NotNull(counter);
    }
}
