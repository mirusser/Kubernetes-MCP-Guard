using System.Diagnostics.Metrics;
using InfraGate.Observer.Diagnostics;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverMetricsTests
{
    [Fact]
    public void Meter_HasExpectedNameAndVersion()
    {
        Assert.Equal(ObserverMetrics.MeterName, ObserverMetrics.Meter.Name);
        Assert.Equal(ObserverMetrics.MeterVersion, ObserverMetrics.Meter.Version);
    }

    [Fact]
    public void CreateCycleCountCounter_CreatesLongCounter()
    {
        using var meter = new Meter("test-meter");
        var counter = ObserverMetrics.CreateCycleCountCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateToolCallsCounter_CreatesLongCounter()
    {
        using var meter = new Meter("test-meter");
        var counter = ObserverMetrics.CreateToolCallsCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateSeverityDisagreementCounter_CreatesLongCounter()
    {
        using var meter = new Meter("test-meter");
        var counter = ObserverMetrics.CreateSeverityDisagreementCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateHandoffFailedCounter_CreatesLongCounter()
    {
        using var meter = new Meter("test-meter");
        var counter = ObserverMetrics.CreateHandoffFailedCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateReportsEmittedCounter_CreatesLongCounter()
    {
        using var meter = new Meter("test-meter");
        var counter = ObserverMetrics.CreateReportsEmittedCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateSnapshotFetchErrorsCounter_CreatesLongCounter()
    {
        using var meter = new Meter("test-meter");
        var counter = ObserverMetrics.CreateSnapshotFetchErrorsCounter(meter);
        Assert.NotNull(counter);
    }

    [Fact]
    public void CreateCycleDurationHistogram_CreatesDoubleHistogram()
    {
        using var meter = new Meter("test-meter");
        var histogram = ObserverMetrics.CreateCycleDurationHistogram(meter);
        Assert.NotNull(histogram);
    }

    [Fact]
    public void CycleCountCounter_RecordsMeasurementWithResultTag()
    {
        using var meter = new Meter(ObserverMetrics.MeterName, ObserverMetrics.MeterVersion);
        var counter = ObserverMetrics.CreateCycleCountCounter(meter);

        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == ObserverMetrics.CycleCountCounterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                recorded.Add(new Measurement<long>(measurement, tags));
            });
        listener.Start();

        counter.Add(1,
            new KeyValuePair<string, object?>(ObserverMetrics.ResultTag, ObserverMetrics.ResultCompleted));

        Assert.Single(recorded);
        Assert.Equal(1L, recorded[0].Value);

        var resultTag = recorded[0].Tags.ToArray().FirstOrDefault(t => t.Key == ObserverMetrics.ResultTag);
        Assert.NotEqual(default, resultTag);
        Assert.Equal(ObserverMetrics.ResultCompleted, resultTag.Value);
    }

    [Fact]
    public void CycleDurationHistogram_RecordsDoubleValue()
    {
        using var meter = new Meter(ObserverMetrics.MeterName, ObserverMetrics.MeterVersion);
        var histogram = ObserverMetrics.CreateCycleDurationHistogram(meter);

        var recorded = new List<Measurement<double>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == ObserverMetrics.CycleDurationHistogramName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, state) =>
            {
                recorded.Add(new Measurement<double>(measurement, tags));
            });
        listener.Start();

        histogram.Record(1234.5);

        Assert.Single(recorded);
        Assert.Equal(1234.5, recorded[0].Value);
    }

    [Fact]
    public void ToolCallsCounter_AccumulatesTotal()
    {
        using var meter = new Meter(ObserverMetrics.MeterName, ObserverMetrics.MeterVersion);
        var counter = ObserverMetrics.CreateToolCallsCounter(meter);

        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == ObserverMetrics.ToolCallsCounterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                recorded.Add(new Measurement<long>(measurement, tags));
            });
        listener.Start();

        counter.Add(3);
        counter.Add(5);

        Assert.Equal(2, recorded.Count);
        Assert.Equal(3L, recorded[0].Value);
        Assert.Equal(5L, recorded[1].Value);
    }

    [Fact]
    public void SeverityDisagreementCounter_Records()
    {
        using var meter = new Meter(ObserverMetrics.MeterName, ObserverMetrics.MeterVersion);
        var counter = ObserverMetrics.CreateSeverityDisagreementCounter(meter);

        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == ObserverMetrics.SeverityDisagreementCounterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                recorded.Add(new Measurement<long>(measurement, tags));
            });
        listener.Start();

        counter.Add(2);

        Assert.Single(recorded);
        Assert.Equal(2L, recorded[0].Value);
    }

    [Fact]
    public void ReportsEmittedCounter_RecordsWithStatusTag()
    {
        using var meter = new Meter(ObserverMetrics.MeterName, ObserverMetrics.MeterVersion);
        var counter = ObserverMetrics.CreateReportsEmittedCounter(meter);

        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == ObserverMetrics.ReportsEmittedCounterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                recorded.Add(new Measurement<long>(measurement, tags));
            });
        listener.Start();

        counter.Add(1,
            new KeyValuePair<string, object?>(ObserverMetrics.StatusTag, "active"));
        counter.Add(1,
            new KeyValuePair<string, object?>(ObserverMetrics.StatusTag, "resolved"));

        Assert.Equal(2, recorded.Count);

        var activeTag = recorded[0].Tags.ToArray().FirstOrDefault(t => t.Key == ObserverMetrics.StatusTag);
        Assert.Equal("active", activeTag.Value);

        var resolvedTag = recorded[1].Tags.ToArray().FirstOrDefault(t => t.Key == ObserverMetrics.StatusTag);
        Assert.Equal("resolved", resolvedTag.Value);
    }

    [Fact]
    public void SnapshotFetchErrorsCounter_RecordsWithToolNameTag()
    {
        using var meter = new Meter(ObserverMetrics.MeterName, ObserverMetrics.MeterVersion);
        var counter = ObserverMetrics.CreateSnapshotFetchErrorsCounter(meter);

        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == ObserverMetrics.SnapshotFetchErrorsCounterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                recorded.Add(new Measurement<long>(measurement, tags));
            });
        listener.Start();

        counter.Add(1,
            new KeyValuePair<string, object?>(ObserverMetrics.ToolNameTag, "get_k8s_events"));

        Assert.Single(recorded);
        Assert.Equal(1L, recorded[0].Value);

        var toolTag = recorded[0].Tags.ToArray().FirstOrDefault(t => t.Key == ObserverMetrics.ToolNameTag);
        Assert.Equal("get_k8s_events", toolTag.Value);
    }

    [Fact]
    public void TagConstants_UseLowercaseSnakeCase()
    {
        Assert.Equal("result", ObserverMetrics.ResultTag);
        Assert.Equal("status", ObserverMetrics.StatusTag);
        Assert.Equal("tool_name", ObserverMetrics.ToolNameTag);
        Assert.Equal("sink_name", ObserverMetrics.SinkNameTag);
    }

    [Fact]
    public void ResultTagValues_AreLowercase()
    {
        Assert.Equal("completed", ObserverMetrics.ResultCompleted);
        Assert.Equal("truncated", ObserverMetrics.ResultTruncated);
        Assert.Equal("error", ObserverMetrics.ResultError);
    }

    [Fact]
    public void AllCounters_UseStaticMeter_WhenNoMeterProvided()
    {
        var cycleCounter = ObserverMetrics.CreateCycleCountCounter();
        var toolCallsCounter = ObserverMetrics.CreateToolCallsCounter();
        var disagreementCounter = ObserverMetrics.CreateSeverityDisagreementCounter();
        var handoffFailedCounter = ObserverMetrics.CreateHandoffFailedCounter();
        var reportsCounter = ObserverMetrics.CreateReportsEmittedCounter();
        var snapshotErrorsCounter = ObserverMetrics.CreateSnapshotFetchErrorsCounter();

        Assert.NotNull(cycleCounter);
        Assert.NotNull(toolCallsCounter);
        Assert.NotNull(disagreementCounter);
        Assert.NotNull(handoffFailedCounter);
        Assert.NotNull(reportsCounter);
        Assert.NotNull(snapshotErrorsCounter);
    }
}
