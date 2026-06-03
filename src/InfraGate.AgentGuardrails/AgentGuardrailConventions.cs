namespace InfraGate.AgentGuardrails;

public static class AgentGuardrailConventions
{
    public const string MeterName = "InfraGate.AgentGuardrails";
    public const string MeterVersion = "1.0";

    public const string ToolCallBlockedCounterName = "infragate.agentguardrails.tool_call.blocked";
    public const string DecisionCounterName = "infragate.agentguardrails.decision";
    public const string ModelVisibleDecisionCounterName = "infragate.agentguardrails.model_visible.decision";
    public const string ModelVisibleDegradedCounterName = "infragate.agentguardrails.model_visible.degraded";
    public const string ModelVisibleEvaluationDurationHistogramName = "infragate.agentguardrails.model_visible.evaluation_duration_ms";

    public const string DefaultQuarantinePlaceholder = "[CONTENT QUARANTINED: suspicious content was withheld before model processing]";
    public const string DefaultBlockedPlaceholder = "[BLOCKED: model ingestion was stopped for security reasons]";

    public static class Tags
    {
        public const string AgentName = "agent.name";
        public const string ToolName = "tool.name";
        public const string GuardrailReason = "guardrail.reason";
        public const string GuardrailOutcome = "guardrail.outcome";
        public const string ModelVisibleSource = "model_visible.source";
        public const string ModelVisibleAction = "model_visible.action";
    }

    public static class Reasons
    {
        public const string ToolNotAllowed = "tool_not_allowed";
        public const string InvalidOperation = "invalid_operation";
        public const string InvalidArguments = "invalid_arguments";
        public const string DedupeInBatch = "dedupe_in_batch";
        public const string ExceededMaximumInputCharacters = "exceeded_maximum_input_characters";
        public const string None = "none";
    }

    public static class Outcomes
    {
        public const string Accepted = "accepted";
        public const string Rejected = "rejected";
    }

    public static class Actions
    {
        public const string Allow = "allow";
        public const string Redact = "redact";
        public const string Quarantine = "quarantine";
        public const string BlockModelIngestion = "block_model_ingestion";
    }

    public static class Sources
    {
        public const string ObserverSnapshot = "observer_snapshot";
        public const string PlannerAnomaly = "planner_anomaly";
        public const string AgentToolResult = "agent_tool_result";
    }
}
