namespace InfraGate.AgentGuardrails;

public sealed record class ModelVisibleContentDecision(
    ModelVisibleContentAction Action,
    string Text,
    IReadOnlyList<string> Categories,
    string Reason,
    string? Digest = null);
