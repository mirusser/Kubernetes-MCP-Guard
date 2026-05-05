using System.Text.RegularExpressions;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway;

public sealed partial class GuardedToolRunner
{
    private const string Warning =
        "Guardrail warning: Potential prompt-injection content was detected. Model-visible high-risk text was redacted where applicable.";

    private readonly IDownstreamMcpClient downstream;
    private readonly PromptInjectionGuard guard;
    private readonly IGuardrailAuditStore auditStore;
    private readonly IHttpContextAccessor? httpContextAccessor;

    public GuardedToolRunner(
        IDownstreamMcpClient downstream,
        PromptInjectionGuard guard,
        IGuardrailAuditStore auditStore)
        : this(downstream, guard, auditStore, httpContextAccessor: null)
    {
    }

    public GuardedToolRunner(
        IDownstreamMcpClient downstream,
        PromptInjectionGuard guard,
        IGuardrailAuditStore auditStore,
        IHttpContextAccessor? httpContextAccessor)
    {
        this.downstream = downstream;
        this.guard = guard;
        this.auditStore = auditStore;
        this.httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var auditIdentity = GetAuditIdentity();
        var requestScan = guard.ScanArguments(arguments);
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

        var downstreamText = await downstream.CallToolAsync(toolName, arguments, cancellationToken);
        var response = guard.SanitizeResponse(downstreamText);
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
