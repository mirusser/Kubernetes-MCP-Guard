namespace InfraGate.McpGateway;

internal sealed record class RedactionResult(
    string Text,
    bool WasRedacted,
    IReadOnlyDictionary<string, int> CountByPattern,
    IReadOnlyList<string> PatternsMatched);
