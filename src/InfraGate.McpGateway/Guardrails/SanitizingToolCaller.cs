using InfraGate.Approvals.Execution;
using InfraGate.McpGateway.Auth;
using ModelContextProtocol.Protocol;

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
        DownstreamCallResult downstreamResult;
        try
        {
            downstreamResult = await inner.CallToolAsync(toolName, arguments, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Downstream tool call '{ToolName}' failed", toolName);
            downstreamResult = DownstreamCallResult.FromText("Tool call failed");
        }

        // Sanitize typed content blocks while preserving structure
        SanitizedContentResult sanitized = PromptInjectionGuard.SanitizeTypedContent(
            downstreamResult.Content,
            downstreamResult.IsError,
            downstreamResult.Meta);

        // Apply sensitive data redaction to each text block
        var redactedBlocks = new List<object>(sanitized.Content.Count);
        bool anyRedacted = false;
        var redactionResults = new List<RedactionResult>();

        foreach (var block in sanitized.Content)
        {
            if (block is TextContentBlock textBlock)
            {
                RedactionResult redacted = redactor.Redact(textBlock.Text);
                redactedBlocks.Add(new TextContentBlock { Text = redacted.Text });
                if (redacted.WasRedacted)
                {
                    anyRedacted = true;
                    redactionResults.Add(redacted);
                }
            }
            else
            {
                // Preserve non-text blocks as-is (though we fail closed on them during sanitization)
                redactedBlocks.Add(block);
            }
        }

        bool anyResponseGuardrail = sanitized.HasFindings || sanitized.ManifestRedacted || anyRedacted || sanitized.IsPolicyError;

        if (anyResponseGuardrail)
        {
            await WriteGuardrailAuditAsync(toolName, sanitized, redactionResults, ct).ConfigureAwait(false);
            GuardrailContext.MarkResponseFindings();
        }

        // Flatten to string for backward compatibility (Task 8 will carry the typed result through)
        return FlattenToText(redactedBlocks);
    }

    private static string FlattenToText(IReadOnlyList<object> blocks)
    {
        var textBlocks = new List<string>();
        foreach (var block in blocks)
        {
            if (block is TextContentBlock textBlock)
            {
                textBlocks.Add(textBlock.Text);
            }
        }

        return string.Join(Environment.NewLine, textBlocks);
    }

    private async Task WriteGuardrailAuditAsync(
        string toolName,
        SanitizedContentResult sanitized,
        IReadOnlyList<RedactionResult> redactionResults,
        CancellationToken ct)
    {
        GatewayAuditIdentity auditIdentity = GatewayAuditIdentityResolver.Resolve(
            httpContextAccessor?.HttpContext?.User);

        try
        {
            // Write policy error audit if present
            if (sanitized.IsPolicyError)
            {
                await auditStore.WriteAsync(
                    new GuardrailAuditEvent(
                        toolName,
                        McpGatewayConventions.GuardrailAudit.ResponseDirection,
                        McpGatewayConventions.GuardrailAudit.PolicyDenyAction,
                        [McpGatewayConventions.GuardrailCategories.UnsupportedContentType],
                        PlanId: null,
                        auditIdentity.Subject,
                        auditIdentity.AuthenticationType,
                        auditIdentity.IdentityKind),
                    ct).ConfigureAwait(false);
            }

            // Write prompt injection / manifest audit if present
            if (sanitized.HasFindings || sanitized.ManifestRedacted)
            {
                string[] categories = sanitized.HasFindings
                    ? sanitized.Findings.Select(f => f.Category).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToArray()
                    : [McpGatewayConventions.GuardrailCategories.ManifestEchoCategory];

                await auditStore.WriteAsync(
                    new GuardrailAuditEvent(
                        toolName,
                        McpGatewayConventions.GuardrailAudit.ResponseDirection,
                        sanitized.HasFindings
                            ? McpGatewayConventions.GuardrailAudit.WarnRedactAction
                            : McpGatewayConventions.GuardrailAudit.RedactManifestAction,
                        categories,
                        PlanId: null,
                        auditIdentity.Subject,
                        auditIdentity.AuthenticationType,
                        auditIdentity.IdentityKind),
                    ct).ConfigureAwait(false);
            }

            // Write sensitive data redaction audits (one per redaction result)
            foreach (var redacted in redactionResults)
            {
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Audit write failure is non-fatal; the response is still returned.
        }
    }
}
