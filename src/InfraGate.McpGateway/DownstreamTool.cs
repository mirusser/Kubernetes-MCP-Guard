using System.Text.Json;

namespace InfraGate.McpGateway;

public sealed record class DownstreamTool(
    string Name,
    string Description,
    bool IsReadOnly,
    bool IsDestructive,
    JsonElement InputSchema);
