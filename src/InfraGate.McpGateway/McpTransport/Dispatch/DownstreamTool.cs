using System.Text.Json;

namespace InfraGate.McpGateway;

public sealed record class DownstreamTool(
    string Name,
    string Description,
    bool IsReadOnly,
    bool IsDestructive,
    JsonElement InputSchema)
{
    /// <summary>
    /// Exact protocol annotations advertised by the downstream. An undefined value means the
    /// caller did not capture the annotation contract and therefore cannot pass strict capability
    /// admission.
    /// </summary>
    public JsonElement Annotations { get; init; }
}
