using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway;

// Single source of truth for which OAuth scopes are required to call or see each gateway tool.
// Consulted by both GatewayToolDispatcher.ListToolsAsync (filter) and CallToolAsyncCore (enforce).
internal static class ToolScopeCatalog
{
    // Returns the set of scopes that permit access to a synthesized/gateway tool.
    // Returns null for downstream tools (callers handle those via ReadOnlyHint).
    internal static IReadOnlyList<string>? GetSynthesizedScopes(string toolName)
    {
        if (toolName.StartsWith(McpGatewayConventions.ToolNames.RequestToolPrefix, StringComparison.Ordinal))
        {
            return [McpGatewayConventions.ToolScopeRequirements.MutationScope, McpGatewayConventions.ToolScopeRequirements.WriteScope];
        }

        return toolName switch
        {
            McpGatewayConventions.ToolNames.ApplyApprovedPlan => [McpGatewayConventions.ToolScopeRequirements.MutationScope, McpGatewayConventions.ToolScopeRequirements.ExecuteScope, McpGatewayConventions.ToolScopeRequirements.WriteScope],
            McpGatewayConventions.ToolNames.GetPlanStatus => [McpGatewayConventions.ToolScopeRequirements.MutationScope, McpGatewayConventions.ToolScopeRequirements.ReadOnlyScope, McpGatewayConventions.ToolScopeRequirements.ReadScope],
            McpGatewayConventions.ToolNames.WaitForPlanApproval => [McpGatewayConventions.ToolScopeRequirements.MutationScope, McpGatewayConventions.ToolScopeRequirements.ExecuteScope, McpGatewayConventions.ToolScopeRequirements.WriteScope],
            McpGatewayConventions.ToolNames.ProposePlan => [McpGatewayConventions.ToolScopeRequirements.MutationScope, McpGatewayConventions.ToolScopeRequirements.ProposeScope, McpGatewayConventions.ToolScopeRequirements.WriteScope],
            _ => null
        };
    }

    // Returns required scopes for any tool (synthesized or downstream). Never returns null.
    // For downstream tools, the caller must supply hasReadOnlyHint (from the tool's annotation).
    internal static IReadOnlyList<string> GetRequiredScopes(string toolName, bool hasReadOnlyHint)
    {
        var synthesized = GetSynthesizedScopes(toolName);
        if (synthesized is not null)
            return synthesized;

        if (hasReadOnlyHint)
            return [McpGatewayConventions.ToolScopeRequirements.MutationScope, McpGatewayConventions.ToolScopeRequirements.ReadOnlyScope, McpGatewayConventions.ToolScopeRequirements.ReadScope];

        return [McpGatewayConventions.ToolScopeRequirements.MutationScope, McpGatewayConventions.ToolScopeRequirements.WriteScope];
    }

    // Returns whether a tool should appear in the scope-filtered tools/list response for this caller.
    internal static bool IsVisibleTo(
        string toolName,
        bool hasReadOnlyHint,
        System.Security.Claims.ClaimsPrincipal user)
    {
        return GetRequiredScopes(toolName, hasReadOnlyHint)
            .Any(scope => GatewayAuthentication.HasRequiredScope(user, scope));
    }
}
