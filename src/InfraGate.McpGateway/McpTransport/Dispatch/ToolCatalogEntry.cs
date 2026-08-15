using System.Text.Json;

namespace InfraGate.McpGateway;

/// <summary>
/// Represents a single tool in the Gateway's published catalog with source ownership,
/// reviewed schema, and policy classification.
/// </summary>
internal sealed record class ToolCatalogEntry(
    string ToolName,
    string SourceId,
    DownstreamTool Tool,
    KubernetesMcpServerRequestPolicy? RequestPolicy,
    KubernetesMcpServerResponsePolicy? ResponsePolicy);
