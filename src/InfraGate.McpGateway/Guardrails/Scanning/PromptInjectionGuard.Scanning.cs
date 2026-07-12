using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InfraGate.McpGateway;

public static partial class PromptInjectionGuard
{
    public static GuardScanResult ScanArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        var findings = new List<GuardrailFinding>();
        var textValues = new List<string>();
        foreach ((string? name, object? value) in arguments)
        {
            ScanValue(value, name, findings, textValues);
        }

        AddCombinedTextFindings(textValues, McpGatewayConventions.GuardrailLocations.CombinedInput, findings);

        return new GuardScanResult(findings);
    }

    private static void ScanValue(object? value, string location, List<GuardrailFinding> findings, List<string> textValues)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                textValues.Add(text);
                AddTextFindings(text, location, findings);
                return;
            case JsonNode node:
                ScanJsonNode(node, location, findings, textValues);
                return;
            case JsonElement element:
                ScanJsonElement(element, location, findings, textValues);
                return;
            case IReadOnlyDictionary<string, object?> dictionary:
                ScanReadOnlyDictionary(dictionary, location, findings, textValues);
                return;
            case IDictionary dictionary:
                ScanDictionary(dictionary, location, findings, textValues);
                return;
            case IEnumerable enumerable:
                ScanEnumerable(enumerable, location, findings, textValues);
                return;
        }
    }

    private static void ScanReadOnlyDictionary(
        IReadOnlyDictionary<string, object?> dictionary,
        string location,
        List<GuardrailFinding> findings,
        List<string> textValues)
    {
        foreach ((string? name, object? child) in dictionary)
        {
            ScanValue(child, $"{location}.{name}", findings, textValues);
        }
    }

    private static void ScanDictionary(
        IDictionary dictionary,
        string location,
        List<GuardrailFinding> findings,
        List<string> textValues)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            ScanValue(entry.Value, $"{location}.{entry.Key}", findings, textValues);
        }
    }

    private static void ScanEnumerable(IEnumerable enumerable, string location, List<GuardrailFinding> findings, List<string> textValues)
    {
        int index = 0;
        foreach (object? item in enumerable)
        {
            ScanValue(item, $"{location}[{index}]", findings, textValues);
            index++;
        }
    }

    private static void ScanJsonElement(JsonElement element, string location, List<GuardrailFinding> findings, List<string> textValues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                string text = element.GetString() ?? string.Empty;
                textValues.Add(text);
                AddTextFindings(text, location, findings);
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    ScanJsonElement(property.Value, $"{location}.{property.Name}", findings, textValues);
                }

                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ScanJsonElement(item, $"{location}[{index}]", findings, textValues);
                    index++;
                }

                break;
        }
    }

    private static void ScanJsonNode(JsonNode node, string location, List<GuardrailFinding> findings, List<string> textValues)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                textValues.Add(text);
                AddTextFindings(text, location, findings);
                break;
            case JsonObject jsonObject:
                ScanJsonObject(jsonObject, location, findings, textValues);
                break;
            case JsonArray jsonArray:
                ScanJsonArray(jsonArray, location, findings, textValues);
                break;
        }
    }

    private static void ScanJsonObject(JsonObject jsonObject, string location, List<GuardrailFinding> findings, List<string> textValues)
    {
        foreach ((string? name, JsonNode? child) in jsonObject)
        {
            if (child is not null)
            {
                ScanJsonNode(child, $"{location}.{name}", findings, textValues);
            }
        }
    }

    private static void ScanJsonArray(JsonArray jsonArray, string location, List<GuardrailFinding> findings, List<string> textValues)
    {
        for (int i = 0; i < jsonArray.Count; i++)
        {
            JsonNode? child = jsonArray[i];
            if (child is not null)
            {
                ScanJsonNode(child, $"{location}[{i}]", findings, textValues);
            }
        }
    }

    private static void AddCombinedTextFindings(
        IReadOnlyList<string> textValues,
        string location,
        List<GuardrailFinding> findings)
    {
        if (textValues.Count < 2)
        {
            return;
        }

        AddTextFindings(string.Join(' ', textValues), location, findings);
    }
}
