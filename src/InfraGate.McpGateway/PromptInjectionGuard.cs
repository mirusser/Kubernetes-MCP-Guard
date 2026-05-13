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

    private const string SupportedK8sResourcePattern = @"(?:apps\/v1|v1)\s+(?:Deployment|Service|ConfigMap)";

    private const string OperationalLinePattern =
        @"^\s*(?:PlanId|Next step|Applied plan|Status|Operation|Namespace|Objects|API operations|Rollout|Current status):\b" +
        @"|^\s*Next step:\s+call " + McpGatewayConventions.ToolNames.ApplyApprovedPlan + @" with this PlanId\.\s*$" +
        @"|^\s*Call " + McpGatewayConventions.ToolNames.ApplyApprovedPlan + @"\b" +
        @"|^\s*-\s+" + SupportedK8sResourcePattern + @"/\S+\b" +
        @"|^\s*(?:Applied|Deleted|Scaled|Restarted)\s+" + SupportedK8sResourcePattern + @"\b" +
        @"|^\s*(?:Deployment rollout|No rollout)\b";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly (string Category, Regex Pattern)[] Patterns =
    [
        (
            McpGatewayConventions.GuardrailCategories.IgnoreInstructions,
            new Regex(
                @"\b(?:ignore|disregard|forget|override)\b.{0,80}\b(?:instructions?|rules?|prompts?|system prompt|developer prompt)\b|\b(?:instructions?|rules?|prompts?)\b.{0,80}\b(?:ignore|disregard|forget|override)\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(McpGatewayConventions.RegexTimeoutMilliseconds))),
        (
            McpGatewayConventions.GuardrailCategories.RevealPrompts,
            new Regex(
                @"\b(?:reveal|show|print|dump|display)\b.{0,80}\b(?:system|developer|hidden)\b.{0,80}\b(?:prompts?|instructions?|messages?)\b|\b(?:system|developer|hidden)\b.{0,80}\b(?:prompts?|instructions?|messages?)\b.{0,80}\b(?:reveal|show|print|dump|display)\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(McpGatewayConventions.RegexTimeoutMilliseconds))),
        (
            McpGatewayConventions.GuardrailCategories.ToolUse,
            new Regex(ToolUsePattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(McpGatewayConventions.RegexTimeoutMilliseconds))),
        (
            McpGatewayConventions.GuardrailCategories.SecretExfiltration,
            new Regex(
                @"\b(?:exfiltrate|leak|send|post|upload|copy)\b.{0,100}\b(?:secrets?|tokens?|passwords?|credentials?|kubeconfig|ssh|api\s*keys?|system prompt)\b|\b(?:secrets?|tokens?|passwords?|credentials?|kubeconfig|ssh|api\s*keys?|system prompt)\b.{0,100}\b(?:exfiltrate|leak|send|post|upload|copy)\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(McpGatewayConventions.RegexTimeoutMilliseconds))),
        (
            McpGatewayConventions.GuardrailCategories.AuthorityOverride,
            new Regex(
                @"\b(?:system|developer|highest priority|authoritative)\b.{0,80}\b(?:instructions?|messages?|prompts?)\b|\byou are now\b|\bact as (?:system|developer)\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(McpGatewayConventions.RegexTimeoutMilliseconds)))
    ];
}
