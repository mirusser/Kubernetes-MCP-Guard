namespace InfraGate.AgentGuardrails;

public sealed record class ModelVisibleContent(
    string Text,
    ModelVisibleContentSource Source,
    string AgentName,
    string? CorrelationId = null,
    string? ToolName = null);
