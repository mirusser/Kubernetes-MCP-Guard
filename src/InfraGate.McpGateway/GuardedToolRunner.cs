using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace InfraGate.McpGateway;

public sealed partial class GuardedToolRunner(
    IDownstreamMcpClient downstream,
    PromptInjectionGuard guard,
    IGuardrailAuditStore auditStore)
{
    private const string Warning =
        "Guardrail warning: Potential prompt-injection content was detected. Model-visible high-risk text was redacted where applicable.";

    public async Task<string> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        return await CallAsync(toolName, arguments, upstreamServer: null, cancellationToken);
    }

    public async Task<string> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        McpServer? upstreamServer,
        CancellationToken cancellationToken)
    {
        var requestScan = guard.ScanArguments(arguments);
        if (requestScan.HasFindings)
        {
            await auditStore.WriteAsync(
                new GuardrailAuditEvent(
                    toolName,
                    "request",
                    "warn",
                    requestScan.Categories,
                    ExtractPlanId(arguments, null)),
                cancellationToken);
        }

        var downstreamText = await downstream.CallToolAsync(toolName, arguments, cancellationToken, upstreamServer);
        var response = guard.SanitizeResponse(downstreamText);
        if (response.HasFindings || response.ManifestRedacted)
        {
            await auditStore.WriteAsync(
                new GuardrailAuditEvent(
                    toolName,
                    "response",
                    response.HasFindings ? "warn_redact" : "redact_manifest",
                    response.HasFindings ? response.Categories : ["manifest-echo"],
                    ExtractPlanId(arguments, response.Text)),
                cancellationToken);
        }

        if (!requestScan.HasFindings && !response.HasFindings)
        {
            return response.Text;
        }

        return $"{Warning}{Environment.NewLine}{Environment.NewLine}{response.Text}";
    }

    private static string? ExtractPlanId(IReadOnlyDictionary<string, object?> arguments, string? text)
    {
        if (arguments.TryGetValue("planId", out var planId) &&
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

        return match.Success ? match.Groups["id"].Value : null;
    }

    [GeneratedRegex(@"(?:PlanId|Applied plan):\s+(?<id>[0-9a-z-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlanIdRegex();
}
