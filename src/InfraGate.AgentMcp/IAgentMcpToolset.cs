namespace InfraGate.AgentMcp;

public interface IAgentMcpToolset : IAsyncDisposable
{
    string GatewayBaseUrl { get; }
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken);

    Task<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken);
}
