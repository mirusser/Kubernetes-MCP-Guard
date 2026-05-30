namespace InfraGate.AgentGuardrails;

public static class AgentGuardrailConventions
{
    public const string MeterName = "InfraGate.AgentGuardrails";
    public const string MeterVersion = "1.0";

    public const string ToolCallBlockedCounterName = "infragate.agentguardrails.tool_call.blocked";
    public const string DecisionCounterName = "infragate.agentguardrails.decision";

    public static class Tags
    {
        public const string AgentName = "agent.name";
        public const string ToolName = "tool.name";
        public const string GuardrailReason = "guardrail.reason";
        public const string GuardrailOutcome = "guardrail.outcome";
    }

    public static class Reasons
    {
        public const string ToolNotAllowed = "tool_not_allowed";
        public const string InvalidOperation = "invalid_operation";
        public const string InvalidArguments = "invalid_arguments";
        public const string DedupeInBatch = "dedupe_in_batch";
        public const string None = "none";
    }

    public static class Outcomes
    {
        public const string Accepted = "accepted";
        public const string Rejected = "rejected";
    }
}
