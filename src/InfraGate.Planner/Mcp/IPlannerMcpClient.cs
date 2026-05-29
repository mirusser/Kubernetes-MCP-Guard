using Microsoft.Extensions.AI;

namespace InfraGate.Planner.Mcp;

internal interface IPlannerMcpClient
{
    string GatewayBaseUrl { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Returns only the whitelisted read-only tools — <c>propose_plan</c> is never included.
    /// </summary>
    Task<IReadOnlyList<AITool>> GetReadOnlyToolsAsync(CancellationToken cancellationToken);
}
