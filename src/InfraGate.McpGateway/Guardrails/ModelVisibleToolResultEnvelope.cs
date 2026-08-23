using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

internal static class ModelVisibleToolResultEnvelope
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Serializes a typed guarded tool call result into a structured envelope.
    /// Content blocks are prefixed with a JSON envelope block containing metadata.
    /// For backward compatibility with response size policy, the envelope is the first block.
    /// </summary>
    public static (IReadOnlyList<object> Content, bool IsError, JsonObject? Meta) CreateTypedEnvelope(
        string toolName,
        TypedGuardedToolCallResult result,
        DateTimeOffset generatedAtUtc)
    {
        // Build envelope metadata
        var envelopeMetadata = new Dictionary<string, object?>(StringComparer.Ordinal)
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
            }
        };

        // Wrap content blocks in the envelope's "untrusted" section
        var wrappedContent = new List<object>(result.Content.Count);
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock textBlock)
            {
                // Embed text blocks within the envelope structure for backward compatibility
                var wrapped = new Dictionary<string, object?>(envelopeMetadata, StringComparer.Ordinal)
                {
                    [McpGatewayConventions.ModelVisibleToolResult.Untrusted] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [McpGatewayConventions.ModelVisibleToolResult.UntrustedPayload] = textBlock.Text,
                    }
                };
                wrappedContent.Add(new TextContentBlock { Text = JsonSerializer.Serialize(wrapped, JsonOptions) });
            }
            else
            {
                // Non-text blocks: preserve as-is (though sanitization should have failed closed on these)
                wrappedContent.Add(block);
            }
        }

        return (wrappedContent, result.IsError, result.Meta);
    }

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
