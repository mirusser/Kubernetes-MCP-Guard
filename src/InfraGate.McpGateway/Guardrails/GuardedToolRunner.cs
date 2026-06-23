using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway;

internal sealed partial class GuardedToolRunner
{
    private static readonly Meter Meter = new(
        McpGatewayConventions.Telemetry.MeterName,
        McpGatewayConventions.Telemetry.MeterVersion);
    private static readonly Counter<long> AuditWriteFailedCounter =
        Meter.CreateCounter<long>(McpGatewayConventions.Telemetry.GuardrailAuditWriteFailedCounterName);

    internal const string Warning =
        "Guardrail warning: Potential prompt-injection content was detected. Model-visible high-risk text was redacted where applicable.";

    private readonly IDownstreamMcpClient downstream;
    private readonly IGuardrailAuditStore auditStore;
    private readonly IHttpContextAccessor? httpContextAccessor;
    private readonly SensitiveDataRedactor redactor;
    private readonly ILogger<GuardedToolRunner> logger;

    internal GuardedToolRunner(
        IDownstreamMcpClient downstream,
        IGuardrailAuditStore auditStore,
        IHttpContextAccessor? httpContextAccessor,
        SensitiveDataRedactor redactor,
        ILogger<GuardedToolRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(downstream);
        ArgumentNullException.ThrowIfNull(auditStore);
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(logger);

        this.downstream = downstream;
        this.auditStore = auditStore;
        this.httpContextAccessor = httpContextAccessor;
        this.redactor = redactor;
        this.logger = logger;
    }

    public async Task<string> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        GuardedToolCallResult result = await CallForModelVisibleResponseAsync(toolName, arguments, cancellationToken)
            .ConfigureAwait(false);

        return !result.HasGuardrailFindings
            ? result.Text
            : FormatWarningResponse(result.Text);
    }

    internal async Task<GuardedToolCallResult> CallForModelVisibleResponseAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        GuardScanResult requestScan = await ScanAndAuditRequestAsync(toolName, arguments, cancellationToken)
            .ConfigureAwait(false);

        string downstreamText;
        try
        {
            downstreamText = await downstream.CallToolAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Downstream call to '{ToolName}' threw an exception", toolName);
            string errorText = $"Tool call failed: {ex.GetType().Name}: {ex.Message}";
            return new GuardedToolCallResult(
                errorText,
                McpGatewayConventions.ModelVisibleToolResult.StatusError,
                requestScan.Categories,
                requestScan.HasFindings
                    ? McpGatewayConventions.GuardrailAudit.WarnAction
                    : McpGatewayConventions.ModelVisibleToolResult.GuardrailActionAllow);
        }

        ResponseSanitizationResult response = await SanitizeAndAuditResponseAsync(toolName, arguments, downstreamText, cancellationToken).ConfigureAwait(false);
        string[] categories = requestScan.Categories
            .Concat(response.Categories)
            .Concat(response.SensitiveDataRedacted ? [McpGatewayConventions.GuardrailCategories.SensitiveData] : [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToArray();

        return new GuardedToolCallResult(
            response.Text,
            McpGatewayConventions.ModelVisibleToolResult.StatusSuccess,
            categories,
            DetermineGuardrailAction(requestScan, response));
    }

    internal async Task<bool> AuditRequestAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        GuardScanResult requestScan = await ScanAndAuditRequestAsync(toolName, arguments, cancellationToken)
            .ConfigureAwait(false);

        return requestScan.HasFindings;
    }

    private async Task<GuardScanResult> ScanAndAuditRequestAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        GuardScanResult requestScan = PromptInjectionGuard.ScanArguments(arguments);
        if (!requestScan.HasFindings)
        {
            return requestScan;
        }

        AuditIdentity auditIdentity = GetAuditIdentity();
        await TryWriteAuditAsync(
            new GuardrailAuditEvent(
                toolName,
                McpGatewayConventions.GuardrailAudit.RequestDirection,
                McpGatewayConventions.GuardrailAudit.WarnAction,
                requestScan.Categories,
                ExtractPlanId(arguments, null),
                auditIdentity.Subject,
                auditIdentity.AuthenticationType,
                auditIdentity.IdentityKind),
            cancellationToken).ConfigureAwait(false);

        return requestScan;
    }

    internal async Task<ResponseSanitizationResult> SanitizeAndAuditResponseAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        string responseText,
        CancellationToken cancellationToken)
    {
        ResponseSanitizationResult response = PromptInjectionGuard.SanitizeResponse(responseText);
        string? planId = ExtractPlanId(arguments, response.Text);
        RedactionResult redacted = redactor.Redact(response.Text);
        ResponseSanitizationResult result = new(
            redacted.Text,
            response.Findings,
            response.ManifestRedacted,
            redacted.WasRedacted);

        if (response.HasFindings || response.ManifestRedacted)
        {
            AuditIdentity auditIdentity = GetAuditIdentity();
            await TryWriteAuditAsync(
                new GuardrailAuditEvent(
                    toolName,
                    McpGatewayConventions.GuardrailAudit.ResponseDirection,
                    response.HasFindings
                        ? McpGatewayConventions.GuardrailAudit.WarnRedactAction
                        : McpGatewayConventions.GuardrailAudit.RedactManifestAction,
                    response.HasFindings
                        ? response.Categories
                        : [McpGatewayConventions.GuardrailCategories.ManifestEchoCategory],
                    planId,
                    auditIdentity.Subject,
                    auditIdentity.AuthenticationType,
                    auditIdentity.IdentityKind),
                cancellationToken).ConfigureAwait(false);
        }

        if (redacted.WasRedacted)
        {
            AuditIdentity auditIdentity = GetAuditIdentity();
            await TryWriteAuditAsync(
                GuardrailAuditEventFactory.SensitiveData(
                    toolName,
                    planId,
                    auditIdentity.Subject,
                    auditIdentity.AuthenticationType,
                    auditIdentity.IdentityKind,
                    redacted),
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private static string DetermineGuardrailAction(GuardScanResult requestScan, ResponseSanitizationResult response)
    {
        if (response.HasFindings)
        {
            return McpGatewayConventions.GuardrailAudit.WarnRedactAction;
        }

        if (response.ManifestRedacted)
        {
            return McpGatewayConventions.GuardrailAudit.RedactManifestAction;
        }

        if (response.SensitiveDataRedacted)
        {
            return McpGatewayConventions.GuardrailAudit.RedactSensitiveDataAction;
        }

        return requestScan.HasFindings
            ? McpGatewayConventions.GuardrailAudit.WarnAction
            : McpGatewayConventions.ModelVisibleToolResult.GuardrailActionAllow;
    }

    private async Task TryWriteAuditAsync(
        GuardrailAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditStore.WriteAsync(auditEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Guardrail audit write failed for {ToolName} {Direction} {Action}.",
                auditEvent.ToolName,
                auditEvent.Direction,
                auditEvent.Action);
            AuditWriteFailedCounter.Add(1,
                new KeyValuePair<string, object?>(McpGatewayConventions.Telemetry.Tags.ToolName, auditEvent.ToolName),
                new KeyValuePair<string, object?>(McpGatewayConventions.Telemetry.Tags.GuardrailDirection, auditEvent.Direction),
                new KeyValuePair<string, object?>(McpGatewayConventions.Telemetry.Tags.GuardrailAction, auditEvent.Action));
        }
    }

    internal static string FormatWarningResponse(string text) =>
        $"{Warning}{Environment.NewLine}{Environment.NewLine}{text}";

    private AuditIdentity GetAuditIdentity()
    {
        GatewayAuditIdentity identity = GatewayAuditIdentityResolver.Resolve(httpContextAccessor?.HttpContext?.User);

        return new AuditIdentity(identity.Subject, identity.AuthenticationType, identity.IdentityKind);
    }

    private static string? ExtractPlanId(IReadOnlyDictionary<string, object?> arguments, string? text)
    {
        if (arguments.TryGetValue(McpGatewayConventions.ToolArguments.PlanId, out object? planId) &&
            planId is string planIdText &&
            !string.IsNullOrWhiteSpace(planIdText))
        {
            return planIdText;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match match = PlanIdRegex().Match(text);

        return match.Success ? match.Groups[McpGatewayConventions.RegexGroups.Id].Value : null;
    }

    [GeneratedRegex(@"(?:PlanId|Applied plan):\s+(?<id>[0-9a-z-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex PlanIdRegex();

    private sealed record class AuditIdentity(string? Subject, string? AuthenticationType, string IdentityKind);
}
