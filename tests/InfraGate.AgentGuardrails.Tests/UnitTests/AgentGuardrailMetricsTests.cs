namespace InfraGate.AgentGuardrails.Tests.UnitTests;

public sealed class AgentGuardrailMetricsTests
{
    [Fact]
    public void RecordToolBlocked_ValidInput_RecordsMeasurementWithExpectedTags()
    {
        using var testMeter = new Meter("test-guardrails-tool-blocked");
        var sut = new AgentGuardrailMetrics(testMeter);

        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == AgentGuardrailConventions.ToolCallBlockedCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recorded.Add(new Measurement<long>(value, tags)));
        listener.Start();

        sut.RecordToolBlocked("observer-ns1", "propose_plan", AgentGuardrailConventions.Reasons.ToolNotAllowed);

        Assert.Single(recorded);
        Assert.Equal(1L, recorded[0].Value);

        var tags = recorded[0].Tags.ToArray();
        Assert.Equal("observer-ns1", tags.First(t => t.Key == AgentGuardrailConventions.Tags.AgentName).Value);
        Assert.Equal("propose_plan", tags.First(t => t.Key == AgentGuardrailConventions.Tags.ToolName).Value);
        Assert.Equal(AgentGuardrailConventions.Reasons.ToolNotAllowed, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailReason).Value);
    }

    [Fact]
    public void RecordDecision_Accepted_RecordsMeasurementWithAcceptedOutcome()
    {
        using var testMeter = new Meter("test-guardrails-decision-accepted");
        var sut = new AgentGuardrailMetrics(testMeter);

        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == AgentGuardrailConventions.DecisionCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recorded.Add(new Measurement<long>(value, tags)));
        listener.Start();

        sut.RecordDecision(GuardrailDecisionOutcome.Accepted, AgentGuardrailConventions.Reasons.None);

        Assert.Single(recorded);
        Assert.Equal(1L, recorded[0].Value);

        var tags = recorded[0].Tags.ToArray();
        Assert.Equal(AgentGuardrailConventions.Outcomes.Accepted, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailOutcome).Value);
        Assert.Equal(AgentGuardrailConventions.Reasons.None, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailReason).Value);
    }

    [Fact]
    public void RecordDecision_Rejected_RecordsMeasurementWithRejectedOutcomeAndReason()
    {
        using var testMeter = new Meter("test-guardrails-decision-rejected");
        var sut = new AgentGuardrailMetrics(testMeter);

        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == AgentGuardrailConventions.DecisionCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recorded.Add(new Measurement<long>(value, tags)));
        listener.Start();

        sut.RecordDecision(GuardrailDecisionOutcome.Rejected, AgentGuardrailConventions.Reasons.InvalidOperation);

        Assert.Single(recorded);
        Assert.Equal(1L, recorded[0].Value);

        var tags = recorded[0].Tags.ToArray();
        Assert.Equal(AgentGuardrailConventions.Outcomes.Rejected, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailOutcome).Value);
        Assert.Equal(AgentGuardrailConventions.Reasons.InvalidOperation, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailReason).Value);
    }

    [Fact]
    public void Constants_AreValid_UseExpectedNamingConventions()
    {
        Assert.StartsWith("infragate.agentguardrails.", AgentGuardrailConventions.ToolCallBlockedCounterName);
        Assert.StartsWith("infragate.agentguardrails.", AgentGuardrailConventions.DecisionCounterName);
        Assert.Equal("agent.name", AgentGuardrailConventions.Tags.AgentName);
        Assert.Equal("tool.name", AgentGuardrailConventions.Tags.ToolName);
        Assert.Equal("guardrail.reason", AgentGuardrailConventions.Tags.GuardrailReason);
        Assert.Equal("guardrail.outcome", AgentGuardrailConventions.Tags.GuardrailOutcome);
    }
}
