using System.Text.Json;
using System.Text.RegularExpressions;

namespace InfraGate.McpGateway;

public static partial class PromptInjectionGuard
{
    public const string RedactedValue = McpGatewayConventions.Redactions.PromptInjectionRisk;

    private const string ToolUsePattern =
        @"\b(?:call|invoke|use|run|execute)\b.{0,80}\b(?:tools?|functions?|mcp|" +
        McpGatewayConventions.ToolNames.ApplyApprovedPlan +
        @"|request_[a-z_]+)\b|\b(?:" +
        McpGatewayConventions.ToolNames.ApplyApprovedPlan +
        @"|request_[a-z_]+)\b.{0,80}\b(?:call|invoke|use|run|execute)\b";

    private const string ResourceReferencePattern =
        @"(?:[A-Za-z0-9_.-]+\/)?[A-Za-z0-9_.-]+\s+[A-Za-z][A-Za-z0-9_.-]*(?:\s+\S+\/\S+|\/\S+)?";

    private const string OperationalLinePattern =
        @"^\s*(?:PlanId|Next step|Applied plan|Status|Operation|Namespace|Objects|API operations|Rollout|Current status):\b" +
        @"|^\s*Next step:\s+call " + McpGatewayConventions.ToolNames.ApplyApprovedPlan + @" with this PlanId\.\s*$" +
        @"|^\s*Call " + McpGatewayConventions.ToolNames.ApplyApprovedPlan + @"\b" +
        @"|^\s*-\s+" + ResourceReferencePattern + @"\b" +
        @"|^\s*(?:Applied|Deleted|Scaled|Restarted)\s+" + ResourceReferencePattern + @"\b" +
        @"|^\s*(?:[A-Za-z][A-Za-z0-9_.-]*\s+rollout|No rollout)\b";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly (string Category, Regex Pattern)[] Patterns =
    [
        (McpGatewayConventions.GuardrailCategories.IgnoreInstructions, IgnoreInstructionsRegex()),
        (McpGatewayConventions.GuardrailCategories.RevealPrompts, RevealPromptsRegex()),
        (McpGatewayConventions.GuardrailCategories.ToolUse, ToolUseRegex()),
        (McpGatewayConventions.GuardrailCategories.SecretExfiltration, SecretExfiltrationRegex()),
        (McpGatewayConventions.GuardrailCategories.AuthorityOverride, AuthorityOverrideRegex())
    ];
}
