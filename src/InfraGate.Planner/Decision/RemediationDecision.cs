namespace InfraGate.Planner.Decision;

internal sealed record class RemediationDecision(
    string OperationType,
    IReadOnlyDictionary<string, object?> Arguments,
    string? Reasoning);
