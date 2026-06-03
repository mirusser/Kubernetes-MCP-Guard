namespace InfraGate.AgentGuardrails;

public sealed class AgentGuardrailMetrics
{
    private static readonly Meter meter = new(AgentGuardrailConventions.MeterName, AgentGuardrailConventions.MeterVersion);

    private readonly Counter<long> toolCallBlockedCounter;
    private readonly Counter<long> decisionCounter;
    private readonly Counter<long> modelVisibleDecisionCounter;
    private readonly Counter<long> modelVisibleDegradedCounter;
    private readonly Histogram<double> modelVisibleEvaluationDurationHistogram;

    public AgentGuardrailMetrics(Meter? meterOverride = null)
    {
        Meter m = meterOverride ?? meter;
        toolCallBlockedCounter = m.CreateCounter<long>(AgentGuardrailConventions.ToolCallBlockedCounterName);
        decisionCounter = m.CreateCounter<long>(AgentGuardrailConventions.DecisionCounterName);
        modelVisibleDecisionCounter = m.CreateCounter<long>(AgentGuardrailConventions.ModelVisibleDecisionCounterName);
        modelVisibleDegradedCounter = m.CreateCounter<long>(AgentGuardrailConventions.ModelVisibleDegradedCounterName);
        modelVisibleEvaluationDurationHistogram = m.CreateHistogram<double>(
            AgentGuardrailConventions.ModelVisibleEvaluationDurationHistogramName,
            unit: "ms");
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

    public void RecordModelVisibleDecision(
        ModelVisibleContentAction action,
        ModelVisibleContentSource source,
        string agentName,
        double evaluationDurationMs)
    {
        string actionValue = action switch
        {
            ModelVisibleContentAction.Allow => AgentGuardrailConventions.Actions.Allow,
            ModelVisibleContentAction.Redact => AgentGuardrailConventions.Actions.Redact,
            ModelVisibleContentAction.Quarantine => AgentGuardrailConventions.Actions.Quarantine,
            ModelVisibleContentAction.BlockModelIngestion => AgentGuardrailConventions.Actions.BlockModelIngestion,
            _ => AgentGuardrailConventions.Actions.Allow,
        };

        string sourceValue = MapSource(source);

        modelVisibleDecisionCounter.Add(1,
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.AgentName, agentName),
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.ModelVisibleSource, sourceValue),
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.ModelVisibleAction, actionValue));

        modelVisibleEvaluationDurationHistogram.Record(evaluationDurationMs,
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.AgentName, agentName),
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.ModelVisibleSource, sourceValue));
    }

    public void RecordModelVisibleDegraded(ModelVisibleContentSource source, string agentName)
    {
        string sourceValue = MapSource(source);

        modelVisibleDegradedCounter.Add(1,
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.AgentName, agentName),
            new KeyValuePair<string, object?>(AgentGuardrailConventions.Tags.ModelVisibleSource, sourceValue));
    }

    private static string MapSource(ModelVisibleContentSource source) =>
        source switch
        {
            ModelVisibleContentSource.ObserverSnapshot => AgentGuardrailConventions.Sources.ObserverSnapshot,
            ModelVisibleContentSource.PlannerAnomaly => AgentGuardrailConventions.Sources.PlannerAnomaly,
            ModelVisibleContentSource.AgentToolResult => AgentGuardrailConventions.Sources.AgentToolResult,
            _ => AgentGuardrailConventions.Sources.ObserverSnapshot,
        };
}
