namespace InfraGate.Planner.Mcp;

internal interface IPlannerMcpClient
{
    string GatewayBaseUrl { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken);
}
