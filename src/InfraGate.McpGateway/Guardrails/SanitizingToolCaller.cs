using InfraGate.Approvals.Execution;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway;

internal sealed class SanitizingToolCaller(
    IDownstreamMcpClient inner,
    IGuardrailAuditStore auditStore,
    IHttpContextAccessor? httpContextAccessor,
    ILogger<SanitizingToolCaller> logger) : IToolCaller
{
    public async Task<string> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct)
    {
        string rawResponse;
        try
        {
            rawResponse = await inner.CallToolAsync(toolName, arguments, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Downstream tool call '{ToolName}' failed", toolName);
            rawResponse = "Tool call failed";
        }

        var sanitized = PromptInjectionGuard.SanitizeResponse(rawResponse ?? string.Empty);

        if (sanitized.HasFindings || sanitized.ManifestRedacted)
        {
            var auditIdentity = GatewayAuditIdentityResolver.Resolve(
                httpContextAccessor?.HttpContext?.User);

            try
            {
                await auditStore.WriteAsync(
                    new GuardrailAuditEvent(
                        toolName,
                        McpGatewayConventions.GuardrailAudit.ResponseDirection,
                        sanitized.HasFindings
                            ? McpGatewayConventions.GuardrailAudit.WarnRedactAction
                            : McpGatewayConventions.GuardrailAudit.RedactManifestAction,
                        sanitized.HasFindings
                            ? sanitized.Categories
                            : [McpGatewayConventions.GuardrailCategories.ManifestEchoCategory],
                        PlanId: null,
                        auditIdentity.Subject,
                        auditIdentity.AuthenticationType,
                        auditIdentity.IdentityKind),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Audit write failure is non-fatal; the response is still returned.
            }

            GuardrailContext.MarkResponseFindings();
        }

        return sanitized.Text;
    }
}
