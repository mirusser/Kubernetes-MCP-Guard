namespace InfraGate.AgentGuardrails;

public sealed class AgentGuardrailMetrics
{
    private static readonly Meter meter = new(AgentGuardrailConventions.MeterName, AgentGuardrailConventions.MeterVersion);

    private readonly Counter<long> toolCallBlockedCounter;
    private readonly Counter<long> decisionCounter;

    public AgentGuardrailMetrics(Meter? meterOverride = null)
    {
        Meter m = meterOverride ?? meter;
        toolCallBlockedCounter = m.CreateCounter<long>(AgentGuardrailConventions.ToolCallBlockedCounterName);
        decisionCounter = m.CreateCounter<long>(AgentGuardrailConventions.DecisionCounterName);
    }

    public void RecordToolBlocked(string agentName, string toolName, string reason)
    {
        toolCallBlockedCounter.Add(1,
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.AgentName, agentName),
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.ToolName, toolName),
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.GuardrailReason, reason));
    }

    public void RecordDecision(GuardrailDecisionOutcome outcome, string reason)
    {
        string outcomeValue = outcome == GuardrailDecisionOutcome.Accepted
            ? AgentGuardrailConventions.Outcomes.Accepted
            : AgentGuardrailConventions.Outcomes.Rejected;

        decisionCounter.Add(1,
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.GuardrailOutcome, outcomeValue),
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.GuardrailReason, reason));
    }
}
