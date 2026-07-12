namespace InfraGate.McpGateway;

internal sealed record class GuardedToolCallResult(
    string Text,
    string Status,
    IReadOnlyList<string> Categories,
    string GuardrailAction)
{
    public bool HasGuardrailFindings => Categories.Count > 0;
}
