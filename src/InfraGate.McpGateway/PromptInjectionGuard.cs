using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace InfraGate.McpGateway;

public sealed partial class PromptInjectionGuard
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
        @"^\s*(?:PlanId|Pending file|Approval file|Plan hash|Next step|Applied plan|Status|Operation|Namespace|Objects|API operations|Rollout|Current status):\b" +
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
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)),
        (
            McpGatewayConventions.GuardrailCategories.RevealPrompts,
            new Regex(
                @"\b(?:reveal|show|print|dump|display)\b.{0,80}\b(?:system|developer|hidden)\b.{0,80}\b(?:prompts?|instructions?|messages?)\b|\b(?:system|developer|hidden)\b.{0,80}\b(?:prompts?|instructions?|messages?)\b.{0,80}\b(?:reveal|show|print|dump|display)\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)),
        (
            McpGatewayConventions.GuardrailCategories.ToolUse,
            new Regex(ToolUsePattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)),
        (
            McpGatewayConventions.GuardrailCategories.SecretExfiltration,
            new Regex(
                @"\b(?:exfiltrate|leak|send|post|upload|copy)\b.{0,100}\b(?:secrets?|tokens?|passwords?|credentials?|kubeconfig|ssh|api\s*keys?|system prompt)\b|\b(?:secrets?|tokens?|passwords?|credentials?|kubeconfig|ssh|api\s*keys?|system prompt)\b.{0,100}\b(?:exfiltrate|leak|send|post|upload|copy)\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)),
        (
            McpGatewayConventions.GuardrailCategories.AuthorityOverride,
            new Regex(
                @"\b(?:system|developer|highest priority|authoritative)\b.{0,80}\b(?:instructions?|messages?|prompts?)\b|\byou are now\b|\bact as (?:system|developer)\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
    ];

    public GuardScanResult ScanArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        var findings = new List<GuardrailFinding>();
        foreach (var (name, value) in arguments)
        {
            ScanValue(value, name, findings);
        }

        return new GuardScanResult(findings);
    }

    public ResponseSanitizationResult SanitizeResponse(string responseText)
    {
        var findings = new List<GuardrailFinding>();
        var manifestRedacted = false;
        var withoutManifest = ManifestBlockRegex().Replace(
            responseText,
            match =>
            {
                manifestRedacted = true;
                AddTextFindings(
                    match.Groups[McpGatewayConventions.RegexGroups.Manifest].Value,
                    McpGatewayConventions.GuardrailLocations.ResponseManifest,
                    findings);

                return $"{match.Groups[McpGatewayConventions.RegexGroups.Prefix].Value}{McpGatewayConventions.Redactions.InspectPendingPlan}";
            });

        if (TryRedactJson(withoutManifest, findings, out var jsonText))
        {
            var changed = manifestRedacted || !string.Equals(withoutManifest, jsonText, StringComparison.Ordinal);

            return new ResponseSanitizationResult(
                changed ? jsonText : responseText,
                findings,
                manifestRedacted);
        }

        var text = RedactSuspiciousLines(withoutManifest, findings, out var lineRedacted);
        var response = manifestRedacted || lineRedacted ? text : responseText;

        return new ResponseSanitizationResult(response, findings, manifestRedacted);
    }

    private static void ScanValue(object? value, string location, List<GuardrailFinding> findings)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                AddTextFindings(text, location, findings);
                return;
            case JsonNode node:
                ScanJsonNode(node, location, findings);
                return;
            case JsonElement element:
                ScanJsonElement(element, location, findings);
                return;
            case IReadOnlyDictionary<string, object?> dictionary:
                foreach (var (name, child) in dictionary)
                {
                    ScanValue(child, $"{location}.{name}", findings);
                }

                return;
            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    ScanValue(entry.Value, $"{location}.{entry.Key}", findings);
                }

                return;
            case IEnumerable enumerable:
                var index = 0;
                foreach (var item in enumerable)
                {
                    ScanValue(item, $"{location}[{index}]", findings);
                    index++;
                }

                return;
        }
    }

    private static void ScanJsonElement(JsonElement element, string location, List<GuardrailFinding> findings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddTextFindings(element.GetString() ?? string.Empty, location, findings);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ScanJsonElement(property.Value, $"{location}.{property.Name}", findings);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ScanJsonElement(item, $"{location}[{index}]", findings);
                    index++;
                }

                break;
        }
    }

    private static void ScanJsonNode(JsonNode node, string location, List<GuardrailFinding> findings)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                AddTextFindings(text, location, findings);
                break;
            case JsonObject jsonObject:
                foreach (var (name, child) in jsonObject)
                {
                    if (child is not null)
                    {
                        ScanJsonNode(child, $"{location}.{name}", findings);
                    }
                }

                break;
            case JsonArray jsonArray:
                for (var i = 0; i < jsonArray.Count; i++)
                {
                    if (jsonArray[i] is not null)
                    {
                        ScanJsonNode(jsonArray[i]!, $"{location}[{i}]", findings);
                    }
                }

                break;
        }
    }

    private static bool TryRedactJson(
        string text,
        List<GuardrailFinding> findings,
        out string redactedText)
    {
        redactedText = text;
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return false;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return false;
        }

        var initialCount = findings.Count;
        root = RedactJsonNode(root, McpGatewayConventions.GuardrailLocations.Response, findings);
        if (findings.Count == initialCount)
        {
            return true;
        }

        redactedText = root?.ToJsonString(JsonOptions) ?? text;
        return true;
    }

    private static JsonNode? RedactJsonNode(JsonNode? node, string location, List<GuardrailFinding> findings)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonValue value when value.TryGetValue<string>(out var text):
                var before = findings.Count;
                AddTextFindings(text, location, findings);

                return findings.Count == before ? node : JsonValue.Create(RedactedValue);
            case JsonObject jsonObject:
                foreach (var (name, child) in jsonObject.ToArray())
                {
                    var redacted = RedactJsonNode(child, $"{location}.{name}", findings);
                    if (!ReferenceEquals(redacted, child))
                    {
                        jsonObject[name] = redacted;
                    }
                }

                return jsonObject;
            case JsonArray jsonArray:
                for (var i = 0; i < jsonArray.Count; i++)
                {
                    var child = jsonArray[i];
                    var redacted = RedactJsonNode(child, $"{location}[{i}]", findings);
                    if (!ReferenceEquals(redacted, child))
                    {
                        jsonArray[i] = redacted;
                    }
                }

                return jsonArray;
            default:
                return node;
        }
    }

    private static string RedactSuspiciousLines(
        string text,
        List<GuardrailFinding> findings,
        out bool lineRedacted)
    {
        lineRedacted = false;
        var lines = LineSplitRegex().Split(text);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (IsLineBreak(line) || IsOperationalLine(line))
            {
                continue;
            }

            var before = findings.Count;
            AddTextFindings(line, $"{McpGatewayConventions.GuardrailLocations.ResponseLine}[{i}]", findings);
            if (findings.Count > before)
            {
                lines[i] = RedactedValue;
                lineRedacted = true;
            }
        }

        return string.Concat(lines);
    }

    private static void AddTextFindings(string text, string location, List<GuardrailFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var (category, pattern) in Patterns)
        {
            if (pattern.IsMatch(text))
            {
                findings.Add(new GuardrailFinding(location, category));
            }
        }
    }

    private static bool IsOperationalLine(string line) =>
        OperationalLineRegex().IsMatch(line);

    private static bool IsLineBreak(string line) =>
        line is "\r" or "\n" or "\r\n";

    [GeneratedRegex(
        @"(?ims)(?<prefix>^[ \t]*Manifest:\s*\r?\n)```(?:ya?ml)?\s*\r?\n(?<manifest>.*?)```+",
        RegexOptions.CultureInvariant)]
    private static partial Regex ManifestBlockRegex();

    [GeneratedRegex(@"(\r\n|\r|\n)", RegexOptions.CultureInvariant)]
    private static partial Regex LineSplitRegex();

    [GeneratedRegex(
        OperationalLinePattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OperationalLineRegex();
}

public sealed record GuardrailFinding(string Location, string Category);

public sealed record GuardScanResult(IReadOnlyList<GuardrailFinding> Findings)
{
    public bool HasFindings => Findings.Count > 0;

    public string[] Categories =>
        Findings.Select(finding => finding.Category).Distinct(StringComparer.Ordinal).OrderBy(category => category).ToArray();
}

public sealed record ResponseSanitizationResult(
    string Text,
    IReadOnlyList<GuardrailFinding> Findings,
    bool ManifestRedacted)
{
    public bool HasFindings => Findings.Count > 0;

    public string[] Categories =>
        Findings.Select(finding => finding.Category).Distinct(StringComparer.Ordinal).OrderBy(category => category).ToArray();
}
