using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;

namespace InfraGate.McpServer.Diff;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
internal static class K8sObjectNormalizer
{
    private const string LastAppliedConfigurationAnnotation = "kubectl.kubernetes.io/last-applied-configuration";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .DisableAliases()
        .Build();

    public static string NormalizeJson(string json)
    {
        var node = JsonNode.Parse(json) ?? new JsonObject();
        RemoveNoisyFields(node);
        var sorted = SortNode(node) ?? new JsonObject();

        return JsonSerializer.Serialize(sorted, JsonOptions);
    }

    public static string ToYaml(string normalizedJson)
    {
        var node = JsonNode.Parse(normalizedJson) ?? new JsonObject();
        var yamlObject = ToPlainObject(node);

        return YamlSerializer.Serialize(yamlObject).TrimEnd();
    }

    private static void RemoveNoisyFields(JsonNode node)
    {
        if (node is not JsonObject root)
        {
            return;
        }

        root.Remove("status");

        if (root["metadata"] is not JsonObject metadata)
        {
            return;
        }

        metadata.Remove("managedFields");
        metadata.Remove("resourceVersion");
        metadata.Remove("uid");
        metadata.Remove("creationTimestamp");
        metadata.Remove("generation");

        if (metadata["annotations"] is JsonObject annotations)
        {
            annotations.Remove(LastAppliedConfigurationAnnotation);
            if (annotations.Count == 0)
            {
                metadata.Remove("annotations");
            }
        }
    }

    private static JsonNode? SortNode(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => SortObject(obj),
            JsonArray array => SortArray(array),
            null => null,
            _ => node.DeepClone()
        };
    }

    private static JsonObject SortObject(JsonObject obj)
    {
        var sorted = new JsonObject();
        foreach (var property in obj.OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            sorted[property.Key] = SortNode(property.Value);
        }

        return sorted;
    }

    private static JsonArray SortArray(JsonArray array)
    {
        var sorted = new JsonArray();
        foreach (var item in array)
        {
            sorted.Add(SortNode(item));
        }

        return sorted;
    }

    private static object? ToPlainObject(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => obj.ToDictionary(
                property => property.Key,
                property => ToPlainObject(property.Value),
                StringComparer.Ordinal),
            JsonArray array => array.Select(ToPlainObject).ToArray(),
            JsonValue value => ToPlainScalar(value),
            _ => null
        };
    }

    private static object? ToPlainScalar(JsonValue value)
    {
        var element = value.GetValue<JsonElement>();

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var number) => number,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }
}
