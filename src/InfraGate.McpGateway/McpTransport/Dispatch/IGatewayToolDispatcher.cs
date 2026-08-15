using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

public interface IGatewayToolDispatcher
{
    Task<ListToolsResult> ListToolsAsync(
        ListToolsRequestParams request,
        CancellationToken ct);

    Task<CallToolResult> CallToolAsync(
        CallToolRequestParams request,
        CancellationToken ct);

    /// <summary>
    /// Re-lists and re-validates a single optional secondary source's tools and atomically swaps
    /// its catalog entries, e.g. after a supervised process restart. A no-op for the mandatory
    /// primary source or an unknown source id. Primary tools are unaffected regardless of outcome;
    /// a failed regeneration leaves the source's previous generation serving.
    /// </summary>
    Task RegenerateSourceAsync(string sourceId, CancellationToken ct);
}
