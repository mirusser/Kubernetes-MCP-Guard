using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

internal interface IProposePlanHandler
{
    Task<CallToolResult> ProposeAsync(
        string operationType,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}
