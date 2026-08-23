using System.Text.Json.Nodes;

namespace InfraGate.McpGateway;

/// <summary>
/// Typed result from a guarded tool call that preserves structured content blocks.
/// </summary>
internal sealed record class TypedGuardedToolCallResult(
    IReadOnlyList<object> Content,
    bool IsError,
    JsonObject? Meta,
    string Status,
    IReadOnlyList<string> Categories,
    string GuardrailAction)
{
    public bool HasGuardrailFindings => Categories.Count > 0;
}
