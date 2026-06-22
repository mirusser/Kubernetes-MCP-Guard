using System.Globalization;
using System.Text.Json;

namespace InfraGate.McpServer.Diff;

internal static class KubernetesObjectMetadataExtractor
{
    public static string? ExtractResourceVersion(string? rawJson)
    {
        if (rawJson is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawJson);
        if (document.RootElement.TryGetProperty("metadata", out var metadata) &&
            metadata.TryGetProperty("resourceVersion", out var resourceVersion) &&
            resourceVersion.ValueKind == JsonValueKind.String)
        {
            return resourceVersion.GetString();
        }

        return null;
    }

    public static string? ExtractGeneration(string? rawJson)
    {
        if (rawJson is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawJson);
        if (document.RootElement.TryGetProperty("metadata", out var metadata) &&
            metadata.TryGetProperty("generation", out var generation) &&
            generation.ValueKind == JsonValueKind.Number &&
            generation.TryGetInt64(out long value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }
}
