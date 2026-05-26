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
    public void CreateLlmTokensCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-planner-metrics");
        var counter = PlannerMetrics.CreateLlmTokensCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateDecisionInvalidOperationCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-planner-metrics");
        var counter = PlannerMetrics.CreateDecisionInvalidOperationCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateDecisionInvalidArgumentsCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-planner-metrics");
        var counter = PlannerMetrics.CreateDecisionInvalidArgumentsCounter(meter);
        Assert.NotNull(counter);
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
    public void CreateHandoffHttpFailedCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-planner-metrics");
        var counter = PlannerMetrics.CreateHandoffHttpFailedCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateHandoffHttpBackpressureCounter_ReturnsNonNull()
    {
        using var meter = new Meter("test-planner-metrics");
        var counter = PlannerMetrics.CreateHandoffHttpBackpressureCounter(meter);
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
        Assert.StartsWith("infragate.planner.", PlannerMetrics.LlmTokensCounterName);
        Assert.StartsWith("infragate.planner.", PlannerMetrics.HandoffHttpFailedCounterName);
        Assert.StartsWith("infragate.planner.", PlannerMetrics.HandoffSinkFailedCounterName);
    }
}
