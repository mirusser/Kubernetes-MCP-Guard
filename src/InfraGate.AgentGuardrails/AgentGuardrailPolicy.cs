namespace InfraGate.AgentGuardrails;

public sealed record class AgentGuardrailPolicy(IReadOnlySet<string> AllowedToolNames);
