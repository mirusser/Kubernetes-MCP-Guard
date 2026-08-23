namespace InfraGate.McpGateway;

/// <summary>
/// Represents the result of validating and publishing a source's tool snapshot.
/// </summary>
internal sealed record class ToolCatalogSnapshot(
    bool IsValid,
    string? DegradedReason,
    IReadOnlyList<ToolCatalogEntry> Entries);
