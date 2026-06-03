namespace InfraGate.AgentGuardrails.Tests.UnitTests;

public sealed class ModelVisibleContentGuardMetricsTests
{
    [Fact]
    public void RecordModelVisibleDecision_Allow_RecordsCounterWithAllowAction()
    {
        using var testMeter = new Meter("test-mv-decision-allow");
        var sut = new AgentGuardrailMetrics(testMeter);

        var recorded = new List<Measurement<long>>();
        using var listener = CreateCounterListener(testMeter,
            AgentGuardrailConventions.ModelVisibleDecisionCounterName, recorded);

        sut.RecordModelVisibleDecision(
            ModelVisibleContentAction.Allow,
            ModelVisibleContentSource.ObserverSnapshot,
            "observer-ns1",
            evaluationDurationMs: 12.5);

        Assert.Single(recorded);
        Assert.Equal(AgentGuardrailConventions.Actions.Allow,
            TagValue(recorded[0], AgentGuardrailConventions.Tags.ModelVisibleAction));
        Assert.Equal(AgentGuardrailConventions.Sources.ObserverSnapshot,
            TagValue(recorded[0], AgentGuardrailConventions.Tags.ModelVisibleSource));
        Assert.Equal("observer-ns1",
            TagValue(recorded[0], AgentGuardrailConventions.Tags.AgentName));
    }

    [Fact]
    public void RecordModelVisibleDecision_Redact_RecordsCounterWithRedactAction()
    {
        using var testMeter = new Meter($"test-mv-decision-redact-{Guid.NewGuid()}");
        var sut = new AgentGuardrailMetrics(testMeter);

        var recorded = new List<Measurement<long>>();
        using var listener = CreateCounterListener(testMeter,
            AgentGuardrailConventions.ModelVisibleDecisionCounterName, recorded);

        sut.RecordModelVisibleDecision(
            ModelVisibleContentAction.Redact,
            ModelVisibleContentSource.AgentToolResult,
            "tool-agent",
            evaluationDurationMs: 8.0);

        Assert.Single(recorded);
        Assert.Equal(AgentGuardrailConventions.Actions.Redact,
            TagValue(recorded[0], AgentGuardrailConventions.Tags.ModelVisibleAction));
        Assert.Equal(AgentGuardrailConventions.Sources.AgentToolResult,
            TagValue(recorded[0], AgentGuardrailConventions.Tags.ModelVisibleSource));
    }

    [Fact]
    public void RecordModelVisibleDecision_Quarantine_RecordsCounterWithQuarantineAction()
    {
        using var testMeter = new Meter("test-mv-decision-quarantine");
        var sut = new AgentGuardrailMetrics(testMeter);

        var recorded = new List<Measurement<long>>();
        using var listener = CreateCounterListener(testMeter,
            AgentGuardrailConventions.ModelVisibleDecisionCounterName, recorded);

        sut.RecordModelVisibleDecision(
            ModelVisibleContentAction.Quarantine,
            ModelVisibleContentSource.PlannerAnomaly,
            "planner-agent",
            evaluationDurationMs: 45.0);

        Assert.Single(recorded);
        Assert.Equal(AgentGuardrailConventions.Actions.Quarantine,
            TagValue(recorded[0], AgentGuardrailConventions.Tags.ModelVisibleAction));
        Assert.Equal(AgentGuardrailConventions.Sources.PlannerAnomaly,
            TagValue(recorded[0], AgentGuardrailConventions.Tags.ModelVisibleSource));
    }

    [Fact]
    public void RecordModelVisibleDecision_Block_RecordsCounterWithBlockAction()
    {
        using var testMeter = new Meter("test-mv-decision-block");
        var sut = new AgentGuardrailMetrics(testMeter);

        var recorded = new List<Measurement<long>>();
        using var listener = CreateCounterListener(testMeter,
            AgentGuardrailConventions.ModelVisibleDecisionCounterName, recorded);

        sut.RecordModelVisibleDecision(
            ModelVisibleContentAction.BlockModelIngestion,
            ModelVisibleContentSource.AgentToolResult,
            "tool-agent",
            evaluationDurationMs: 5.0);

        Assert.Equal(AgentGuardrailConventions.Actions.BlockModelIngestion,
            TagValue(recorded[0], AgentGuardrailConventions.Tags.ModelVisibleAction));
        Assert.Equal(AgentGuardrailConventions.Sources.AgentToolResult,
            TagValue(recorded[0], AgentGuardrailConventions.Tags.ModelVisibleSource));
    }

    [Fact]
    public void RecordModelVisibleDecision_RecordsLatencyHistogram()
    {
        using var testMeter = new Meter($"test-mv-latency-{Guid.NewGuid()}");
        var sut = new AgentGuardrailMetrics(testMeter);

        int callCount = 0;
        double recordedValue = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == testMeter &&
                instrument.Name == AgentGuardrailConventions.ModelVisibleEvaluationDurationHistogramName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) =>
        {
            Interlocked.Increment(ref callCount);
            Interlocked.Exchange(ref recordedValue, value);
        });
        listener.Start();

        sut.RecordModelVisibleDecision(
            ModelVisibleContentAction.Allow,
            ModelVisibleContentSource.ObserverSnapshot,
            "observer-ns1",
            evaluationDurationMs: 99.0);

        Assert.Equal(1, callCount);
        Assert.Equal(99.0, recordedValue);
    }

    [Fact]
    public void RecordModelVisibleDegraded_RecordsDegradedCounter()
    {
        using var testMeter = new Meter("test-mv-degraded");
        var sut = new AgentGuardrailMetrics(testMeter);

        var recorded = new List<Measurement<long>>();
        using var listener = CreateCounterListener(testMeter,
            AgentGuardrailConventions.ModelVisibleDegradedCounterName, recorded);

        sut.RecordModelVisibleDegraded(ModelVisibleContentSource.PlannerAnomaly, "planner-agent");

        Assert.Single(recorded);
        Assert.Equal(1L, recorded[0].Value);
        Assert.Equal(AgentGuardrailConventions.Sources.PlannerAnomaly,
            TagValue(recorded[0], AgentGuardrailConventions.Tags.ModelVisibleSource));
        Assert.Equal("planner-agent",
            TagValue(recorded[0], AgentGuardrailConventions.Tags.AgentName));
    }

    [Fact]
    public void Constants_ModelVisibleInstruments_UseExpectedNamingConventions()
    {
        Assert.StartsWith("infragate.agentguardrails.", AgentGuardrailConventions.ModelVisibleDecisionCounterName);
        Assert.StartsWith("infragate.agentguardrails.", AgentGuardrailConventions.ModelVisibleDegradedCounterName);
        Assert.StartsWith("infragate.agentguardrails.", AgentGuardrailConventions.ModelVisibleEvaluationDurationHistogramName);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MeterListener CreateCounterListener(
        Meter meter, string instrumentName, List<Measurement<long>> sink)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == instrumentName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            sink.Add(new Measurement<long>(value, tags)));
        listener.Start();
        return listener;
    }

    private static object? TagValue(Measurement<long> m, string key)
    {
        var tags = m.Tags.ToArray();
        return tags.First(t => t.Key == key).Value;
    }
}
