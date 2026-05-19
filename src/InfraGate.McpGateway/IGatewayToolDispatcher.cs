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
}
