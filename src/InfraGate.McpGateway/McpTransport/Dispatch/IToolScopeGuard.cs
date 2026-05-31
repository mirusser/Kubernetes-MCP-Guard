using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

internal interface IToolScopeGuard
{
    Task<CallToolResult?> RequireAnyScopeAsync(string toolName, params string[] requiredScopes);
}
