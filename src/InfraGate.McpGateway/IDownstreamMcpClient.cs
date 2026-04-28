using ModelContextProtocol.Server;

namespace InfraGate.McpGateway;

public interface IDownstreamMcpClient
{
    Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken,
        McpServer? upstreamServer = null);
}
