using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InfraGate.McpGateway;

public sealed partial class PromptInjectionGuard
{
    public GuardScanResult ScanArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        var findings = new List<GuardrailFinding>();
        foreach (var (name, value) in arguments)
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
                    var child = jsonArray[i];
                    if (child is not null)
                    {
                        ScanJsonNode(child, $"{location}[{i}]", findings);
                    }
                }

                break;
        }
    }
}
