using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InfraGate.McpGateway;

public static partial class PromptInjectionGuard
{
    public static GuardScanResult ScanArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        var findings = new List<GuardrailFinding>();
        foreach ((string? name, object? value) in arguments)
        {
            ScanValue(value, name, findings);
        }

        return new GuardScanResult(findings);
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
                ScanReadOnlyDictionary(dictionary, location, findings);
                return;
            case IDictionary dictionary:
                ScanDictionary(dictionary, location, findings);
                return;
            case IEnumerable enumerable:
                ScanEnumerable(enumerable, location, findings);
                return;
        }
    }

    private static void ScanReadOnlyDictionary(
        IReadOnlyDictionary<string, object?> dictionary,
        string location,
        List<GuardrailFinding> findings)
    {
        foreach ((string? name, object? child) in dictionary)
        {
            ScanValue(child, $"{location}.{name}", findings);
        }
    }

    private static void ScanDictionary(IDictionary dictionary, string location, List<GuardrailFinding> findings)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            ScanValue(entry.Value, $"{location}.{entry.Key}", findings);
        }
    }

    private static void ScanEnumerable(IEnumerable enumerable, string location, List<GuardrailFinding> findings)
    {
        int index = 0;
        foreach (object? item in enumerable)
        {
            ScanValue(item, $"{location}[{index}]", findings);
            index++;
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
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    ScanJsonElement(property.Value, $"{location}.{property.Name}", findings);
                }

                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
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
                ScanJsonObject(jsonObject, location, findings);
                break;
            case JsonArray jsonArray:
                ScanJsonArray(jsonArray, location, findings);
                break;
        }
    }

    private static void ScanJsonObject(JsonObject jsonObject, string location, List<GuardrailFinding> findings)
    {
        foreach ((string? name, JsonNode? child) in jsonObject)
        {
            if (child is not null)
            {
                ScanJsonNode(child, $"{location}.{name}", findings);
            }
        }
    }

    private static void ScanJsonArray(JsonArray jsonArray, string location, List<GuardrailFinding> findings)
    {
        for (int i = 0; i < jsonArray.Count; i++)
        {
            JsonNode? child = jsonArray[i];
            if (child is not null)
            {
                ScanJsonNode(child, $"{location}[{i}]", findings);
            }
        }
    }
}
