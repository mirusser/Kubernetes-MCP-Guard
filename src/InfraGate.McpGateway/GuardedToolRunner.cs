using System.Text.RegularExpressions;
using InfraGate.McpGateway.Auth;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpGateway;

public sealed partial class GuardedToolRunner
{
    private const string Warning =
        "Guardrail warning: Potential prompt-injection content was detected. Model-visible high-risk text was redacted where applicable.";

    private readonly IDownstreamMcpClient downstream;
    private readonly IGuardrailAuditStore auditStore;
    private readonly IHttpContextAccessor? httpContextAccessor;
    private readonly ILogger<GuardedToolRunner> logger;

    public GuardedToolRunner(
        IDownstreamMcpClient downstream,
        IGuardrailAuditStore auditStore,
        ILogger<GuardedToolRunner> logger)
        : this(downstream, auditStore, httpContextAccessor: null, logger)
    {
    }

    public GuardedToolRunner(
        IDownstreamMcpClient downstream,
        IGuardrailAuditStore auditStore,
        IHttpContextAccessor? httpContextAccessor,
        ILogger<GuardedToolRunner> logger)
    {
        this.downstream = downstream;
        this.auditStore = auditStore;
        this.httpContextAccessor = httpContextAccessor;
        this.logger = logger;
    }

    public async Task<string> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var auditIdentity = GetAuditIdentity();
        var requestScan = PromptInjectionGuard.ScanArguments(arguments);
        if (requestScan.HasFindings)
        {
            await auditStore.WriteAsync(
                new GuardrailAuditEvent(
                    toolName,
                    McpGatewayConventions.GuardrailAudit.RequestDirection,
                    McpGatewayConventions.GuardrailAudit.WarnAction,
                    requestScan.Categories,
                    ExtractPlanId(arguments, null),
                    auditIdentity.Subject,
                    auditIdentity.AuthenticationType),
                cancellationToken);
        }

        string downstreamText;
        try
        {
            downstreamText = await downstream.CallToolAsync(toolName, arguments, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Downstream call to '{ToolName}' threw an exception", toolName);
            return $"Tool call failed: {ex.GetType().Name}: {ex.Message}";
        }
        var response = PromptInjectionGuard.SanitizeResponse(downstreamText);
        if (response.HasFindings || response.ManifestRedacted)
        {
            await auditStore.WriteAsync(
                new GuardrailAuditEvent(
                    toolName,
                    McpGatewayConventions.GuardrailAudit.ResponseDirection,
                    response.HasFindings
                        ? McpGatewayConventions.GuardrailAudit.WarnRedactAction
                        : McpGatewayConventions.GuardrailAudit.RedactManifestAction,
                    response.HasFindings
                        ? response.Categories
                        : [McpGatewayConventions.GuardrailCategories.ManifestEchoCategory],
                    ExtractPlanId(arguments, response.Text),
                    auditIdentity.Subject,
                    auditIdentity.AuthenticationType),
                cancellationToken);
        }

        if (!requestScan.HasFindings && !response.HasFindings)
        {
            return response.Text;
        }

        return $"{Warning}{Environment.NewLine}{Environment.NewLine}{response.Text}";
    }

    public Task<string> CallWithRequesterAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var auditIdentity = GetAuditIdentity();
        if (string.IsNullOrWhiteSpace(auditIdentity.Subject))
        {
            return Task.FromResult("Refused: mutation plan creation requires an authenticated OAuth subject.");
        }

        var downstreamArguments = new Dictionary<string, object?>(arguments)
        {
            [McpGatewayConventions.ToolArguments.RequesterSubject] = auditIdentity.Subject,
            [McpGatewayConventions.ToolArguments.RequesterAuthenticationType] = auditIdentity.AuthenticationType
        };

        return CallAsync(toolName, downstreamArguments, cancellationToken);
    }

    private AuditIdentity GetAuditIdentity()
    {
        var identity = GatewayAuditIdentityResolver.Resolve(httpContextAccessor?.HttpContext?.User);

        return new AuditIdentity(identity.Subject, identity.AuthenticationType);
    }

    private static string? ExtractPlanId(IReadOnlyDictionary<string, object?> arguments, string? text)
    {
        if (arguments.TryGetValue(McpGatewayConventions.ToolArguments.PlanId, out var planId) &&
            planId is string planIdText &&
            !string.IsNullOrWhiteSpace(planIdText))
        {
            return planIdText;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = PlanIdRegex().Match(text);

        return match.Success ? match.Groups[McpGatewayConventions.RegexGroups.Id].Value : null;
    }

    [GeneratedRegex(@"(?:PlanId|Applied plan):\s+(?<id>[0-9a-z-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex PlanIdRegex();

    private sealed record AuditIdentity(string? Subject, string? AuthenticationType);
}
