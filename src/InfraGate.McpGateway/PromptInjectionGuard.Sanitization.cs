using System.Text.Json;
using System.Text.Json.Nodes;

namespace InfraGate.McpGateway;

public sealed partial class PromptInjectionGuard
{
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
}
