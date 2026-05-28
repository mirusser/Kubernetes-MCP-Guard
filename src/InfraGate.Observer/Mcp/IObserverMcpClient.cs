namespace InfraGate.Observer.Mcp;

internal interface IObserverMcpClient
{
    string GatewayBaseUrl { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<string?> GetToolResultAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken);
}
