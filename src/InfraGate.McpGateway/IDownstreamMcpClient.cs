namespace InfraGate.McpGateway;

public interface IDownstreamMcpClient
{
    Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken);
}
