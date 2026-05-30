using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

internal interface IToolScopeGuard
{
    Task<CallToolResult?> RequireAnyToolScopeAsync(string toolName);
    Task<CallToolResult?> RequireMutationScopeAsync(string toolName);
    Task<CallToolResult?> RequireAnyScopeAsync(string toolName, params string[] requiredScopes);
}
