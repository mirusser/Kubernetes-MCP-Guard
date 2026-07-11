using InfraGate.Approvals.Execution;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway;

internal sealed class SanitizingToolCaller : IToolCaller
{
    private readonly IDownstreamMcpClient inner;
    private readonly IGuardrailAuditStore auditStore;
    private readonly IHttpContextAccessor? httpContextAccessor;
    private readonly SensitiveDataRedactor redactor;
    private readonly ILogger<SanitizingToolCaller> logger;

    internal SanitizingToolCaller(
        IDownstreamMcpClient inner,
        IGuardrailAuditStore auditStore,
        IHttpContextAccessor? httpContextAccessor,
        SensitiveDataRedactor redactor,
        ILogger<SanitizingToolCaller> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(auditStore);
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(logger);

        this.inner = inner;
        this.auditStore = auditStore;
        this.httpContextAccessor = httpContextAccessor;
        this.redactor = redactor;
        this.logger = logger;
    }

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

        ResponseSanitizationResult sanitized = PromptInjectionGuard.SanitizeResponse(rawResponse ?? string.Empty);
        RedactionResult redacted = redactor.Redact(sanitized.Text);
        bool anyResponseGuardrail = sanitized.HasFindings || sanitized.ManifestRedacted || redacted.WasRedacted;

        if (anyResponseGuardrail)
        {
            await WriteGuardrailAuditAsync(toolName, sanitized, redacted, ct).ConfigureAwait(false);
            GuardrailContext.MarkResponseFindings();
        }

        return redacted.Text;
    }

    private async Task WriteGuardrailAuditAsync(
        string toolName,
        ResponseSanitizationResult sanitized,
        RedactionResult redacted,
        CancellationToken ct)
    {
        GatewayAuditIdentity auditIdentity = GatewayAuditIdentityResolver.Resolve(
            httpContextAccessor?.HttpContext?.User);

        try
        {
            if (sanitized.HasFindings || sanitized.ManifestRedacted)
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

            if (redacted.WasRedacted)
            {
                await auditStore.WriteAsync(
                    GuardrailAuditEventFactory.SensitiveData(
                        toolName,
                        planId: null,
                        auditIdentity.Subject,
                        auditIdentity.AuthenticationType,
                        auditIdentity.IdentityKind,
                        redacted),
                    ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Audit write failure is non-fatal; the response is still returned.
        }
    }
}
