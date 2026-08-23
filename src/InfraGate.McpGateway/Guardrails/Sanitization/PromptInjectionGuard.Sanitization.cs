using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

public static partial class PromptInjectionGuard
{
    /// <summary>
    /// Sanitizes typed content blocks from a downstream call result, preserving block structure.
    /// </summary>
    internal static SanitizedContentResult SanitizeTypedContent(
        IReadOnlyList<object> content,
        bool isError,
        JsonObject? meta)
    {
        var sanitizedBlocks = new List<object>(content.Count);
        var allFindings = new List<GuardrailFinding>();
        bool anyManifestRedacted = false;

        foreach (var block in content)
        {
            if (block is TextContentBlock textBlock)
            {
                ResponseSanitizationResult sanitized = SanitizeResponse(textBlock.Text);
                sanitizedBlocks.Add(new TextContentBlock { Text = sanitized.Text });
                allFindings.AddRange(sanitized.Findings);
                anyManifestRedacted = anyManifestRedacted || sanitized.ManifestRedacted;
            }
            else
            {
                // Unsupported content type - fail closed
                return SanitizedContentResult.CreatePolicyError(
                    $"Unsupported content type: {block?.GetType().Name ?? "null"}");
            }
        }

        return new SanitizedContentResult(
            sanitizedBlocks,
            isError,
            meta,
            allFindings,
            anyManifestRedacted);
    }

    public static ResponseSanitizationResult SanitizeResponse(string responseText)
    {
        var findings = new List<GuardrailFinding>();
        bool manifestRedacted = false;
        string withoutManifest = ManifestBlockRegex().Replace(
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
        string withoutSensitiveMetadata = RedactSensitivePlanMetadataLines(withoutManifest, out bool metadataRedacted);

        if (TryRedactJson(withoutSensitiveMetadata, findings, out string? jsonText))
        {
            bool changed = manifestRedacted ||
                metadataRedacted ||
                !string.Equals(withoutSensitiveMetadata, jsonText, StringComparison.Ordinal);

            return new ResponseSanitizationResult(
                changed ? jsonText : responseText,
                findings,
                manifestRedacted);
        }

        string text = RedactSuspiciousLines(withoutSensitiveMetadata, findings, out bool lineRedacted);
        string response = manifestRedacted || metadataRedacted || lineRedacted ? text : responseText;

        return new ResponseSanitizationResult(response, findings, manifestRedacted);
    }

    private static bool TryRedactJson(
        string text,
        List<GuardrailFinding> findings,
        out string redactedText)
    {
        redactedText = text;
        string trimmed = text.TrimStart();
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

        var textValues = new List<string>();
        CollectJsonTextValues(root, textValues);

        int initialCount = findings.Count;
        root = RedactJsonNode(root, McpGatewayConventions.GuardrailLocations.Response, findings);
        AddCombinedTextFindings(textValues, McpGatewayConventions.GuardrailLocations.ResponseCombined, findings);
        if (findings.Count == initialCount)
        {
            return true;
        }

        redactedText = root?.ToJsonString(JsonOptions) ?? text;
        return true;
    }

    private static void CollectJsonTextValues(JsonNode? node, List<string> textValues)
    {
        switch (node)
        {
            case null:
                return;
            case JsonValue value when value.TryGetValue<string>(out var text):
                textValues.Add(text);
                return;
            case JsonObject jsonObject:
                foreach ((string? _, JsonNode? child) in jsonObject)
                {
                    CollectJsonTextValues(child, textValues);
                }

                return;
            case JsonArray jsonArray:
                foreach (JsonNode? child in jsonArray)
                {
                    CollectJsonTextValues(child, textValues);
                }

                return;
        }
    }

    private static JsonNode? RedactJsonNode(JsonNode? node, string location, List<GuardrailFinding> findings)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonValue value when value.TryGetValue<string>(out var text):
                return RedactJsonValue(node, text, location, findings);
            case JsonObject jsonObject:
                RedactJsonObject(jsonObject, location, findings);
                return jsonObject;
            case JsonArray jsonArray:
                RedactJsonArray(jsonArray, location, findings);
                return jsonArray;
            default:
                return node;
        }
    }

    private static JsonNode? RedactJsonValue(
        JsonNode node,
        string text,
        string location,
        List<GuardrailFinding> findings)
    {
        int before = findings.Count;
        AddTextFindings(text, location, findings);

        return findings.Count == before ? node : JsonValue.Create(RedactedValue);
    }

    private static void RedactJsonObject(JsonObject jsonObject, string location, List<GuardrailFinding> findings)
    {
        foreach ((string? name, JsonNode? child) in jsonObject.ToArray())
        {
            JsonNode? redacted = RedactJsonNode(child, $"{location}.{name}", findings);
            if (!ReferenceEquals(redacted, child))
            {
                jsonObject[name] = redacted;
            }
        }
    }

    private static void RedactJsonArray(JsonArray jsonArray, string location, List<GuardrailFinding> findings)
    {
        for (int i = 0; i < jsonArray.Count; i++)
        {
            JsonNode? child = jsonArray[i];
            JsonNode? redacted = RedactJsonNode(child, $"{location}[{i}]", findings);
            if (!ReferenceEquals(redacted, child))
            {
                jsonArray[i] = redacted;
            }
        }
    }

    private static string RedactSuspiciousLines(
        string text,
        List<GuardrailFinding> findings,
        out bool lineRedacted)
    {
        lineRedacted = false;
        string[] lines = LineSplitRegex().Split(text);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (IsLineBreak(line) || IsOperationalLine(line))
            {
                continue;
            }

            int before = findings.Count;
            AddTextFindings(line, $"{McpGatewayConventions.GuardrailLocations.ResponseLine}[{i}]", findings);
            if (findings.Count > before)
            {
                lines[i] = RedactedValue;
                lineRedacted = true;
            }
        }

        return string.Concat(lines);
    }

    private static string RedactSensitivePlanMetadataLines(string text, out bool metadataRedacted)
    {
        metadataRedacted = false;
        string[] lines = LineSplitRegex().Split(text);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (IsLineBreak(line) || !SensitivePlanMetadataLineRegex().IsMatch(line))
            {
                continue;
            }

            lines[i] = McpGatewayConventions.Redactions.SensitivePlanMetadata;
            metadataRedacted = true;
        }

        return string.Concat(lines);
    }
}
