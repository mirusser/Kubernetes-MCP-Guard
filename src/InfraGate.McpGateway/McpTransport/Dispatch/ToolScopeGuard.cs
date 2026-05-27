using InfraGate.McpGateway.Auth;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpGateway;

internal sealed class ToolScopeGuard(
    IHttpContextAccessor httpContextAccessor,
    IGuardrailAuditStore auditStore,
    ILogger<ToolScopeGuard> logger) : IToolScopeGuard
{
    public async Task<CallToolResult?> RequireAnyToolScopeAsync(string toolName)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return ErrorResult(
                McpGatewayMessages.Authorization.RequiresSession(toolName));
        }

        return await RequireAnyScopeAsync(
            toolName,
            McpGatewayConventions.ToolScopeRequirements.MutationScope,
            McpGatewayConventions.ToolScopeRequirements.ReadOnlyScope).ConfigureAwait(false);
    }

    public async Task<CallToolResult?> RequireMutationScopeAsync(string toolName)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return ErrorResult(
                McpGatewayMessages.Authorization.RequiresAuthenticatedSession(toolName, McpGatewayConventions.ToolScopeRequirements.MutationScope));
        }

        if (!GatewayAuthentication.HasRequiredScope(user, McpGatewayConventions.ToolScopeRequirements.MutationScope))
        {
            return await DenyAndAuditAsync(toolName, McpGatewayConventions.ToolScopeRequirements.MutationScope).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<CallToolResult?> RequireAnyScopeAsync(string toolName, params string[] requiredScopes)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return ErrorResult(
                McpGatewayMessages.Authorization.RequiresOneOfScopes(toolName, string.Join(", ", requiredScopes)));
        }

        if (!requiredScopes.Any(scope => GatewayAuthentication.HasRequiredScope(user, scope)))
        {
            return await DenyAndAuditAsync(toolName, string.Join(" or ", requiredScopes)).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<CallToolResult?> DenyAndAuditAsync(string toolName, string requiredScope)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return ErrorResult(
                McpGatewayMessages.Authorization.RequiresAuthenticatedSession(toolName, requiredScope));
        }

        var identity = GatewayAuditIdentityResolver.Resolve(user);

        logger.LogWarning(
            "Tool '{ToolName}' denied: caller lacks required scope '{RequiredScope}'.",
            toolName,
            requiredScope);

        var auditEvent = new GuardrailAuditEvent(
            toolName,
            McpGatewayConventions.GuardrailAudit.RequestDirection,
            McpGatewayConventions.GuardrailAudit.DenyAction,
            [McpGatewayConventions.GuardrailCategories.ScopeDenied],
            PlanId: null,
            identity.Subject,
            identity.AuthenticationType,
            identity.IdentityKind);

        await auditStore.WriteAsync(auditEvent, CancellationToken.None).ConfigureAwait(false);

        return ErrorResult(
            McpGatewayMessages.Authorization.RequiresScope(toolName, requiredScope));
    }

    private static CallToolResult ErrorResult(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }]
    };
}
