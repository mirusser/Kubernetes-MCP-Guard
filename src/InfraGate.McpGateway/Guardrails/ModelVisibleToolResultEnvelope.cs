using System.Text.Json;

namespace InfraGate.McpGateway;

internal static class ModelVisibleToolResultEnvelope
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Serialize(string toolName, GuardedToolCallResult result, DateTimeOffset generatedAtUtc)
    {
        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpGatewayConventions.ModelVisibleToolResult.SchemaVersion] = 1,
            [McpGatewayConventions.ModelVisibleToolResult.Kind] = McpGatewayConventions.ModelVisibleToolResult.KindValue,
            [McpGatewayConventions.ModelVisibleToolResult.ToolNameKey] = toolName,
            [McpGatewayConventions.ModelVisibleToolResult.Source] = McpGatewayConventions.ModelVisibleToolResult.SourceReadOnlyToolValue,
            [McpGatewayConventions.ModelVisibleToolResult.GeneratedAtUtc] = generatedAtUtc,
            [McpGatewayConventions.ModelVisibleToolResult.Status] = result.Status,
            [McpGatewayConventions.ModelVisibleToolResult.Guardrail] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [McpGatewayConventions.ModelVisibleToolResult.GuardrailAction] = result.GuardrailAction,
                [McpGatewayConventions.ModelVisibleToolResult.GuardrailCategoriesKey] = result.Categories,
            },
            [McpGatewayConventions.ModelVisibleToolResult.Untrusted] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [McpGatewayConventions.ModelVisibleToolResult.UntrustedPayload] = result.Text,
            },
        };

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }
}
