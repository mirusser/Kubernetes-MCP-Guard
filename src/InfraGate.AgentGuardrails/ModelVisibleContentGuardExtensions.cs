using System.Text.Json;
using System.Text.Json.Nodes;
using InfraGate;

namespace InfraGate.AgentGuardrails;

public static class ModelVisibleContentGuardExtensions
{
    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web);

    public static AIAgentBuilder UseModelVisibleContentGuard(
        this AIAgentBuilder builder,
        IModelVisibleContentGuard contentGuard,
        string agentName)
    {
        return builder.Use(async (innerAgent, context, next, cancellationToken) =>
        {
            var rawResult = await next(context, cancellationToken).ConfigureAwait(false);

            string resultText = ToModelVisibleText(rawResult);

            if (string.IsNullOrEmpty(resultText))
                return rawResult;

            string toolName = context.Function?.Name ?? string.Empty;
            var guardContent = new ModelVisibleContent(
                resultText,
                ModelVisibleContentSource.AgentToolResult,
                agentName,
                ToolName: toolName);

            var decision = await contentGuard.EvaluateAsync(guardContent, cancellationToken)
                .ConfigureAwait(false);

            return ApplyDecision(resultText, decision);
        });
    }

    private static string ApplyDecision(string originalText, ModelVisibleContentDecision decision)
    {
        if (decision.Action == ModelVisibleContentAction.Allow)
        {
            return decision.Text;
        }

        if (TryReplaceEnvelopePayload(originalText, decision.Text, out string envelopedDecisionText))
        {
            return envelopedDecisionText;
        }

        return decision.Text;
    }

    private static bool TryReplaceEnvelopePayload(string originalText, string replacementText, out string envelopedDecisionText)
    {
        envelopedDecisionText = string.Empty;

        ReadOnlySpan<char> trimmedText = originalText.AsSpan().TrimStart();
        if (trimmedText.IsEmpty || trimmedText[0] != '{')
        {
            return false;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(originalText);
            if (node is not JsonObject envelope || !IsModelVisibleToolResultEnvelope(envelope))
            {
                return false;
            }

            if (envelope[ModelVisibleToolResultConventions.Untrusted] is not JsonObject untrusted)
            {
                return false;
            }

            untrusted[ModelVisibleToolResultConventions.UntrustedPayload] = replacementText;
            envelopedDecisionText = envelope.ToJsonString(EnvelopeJsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsModelVisibleToolResultEnvelope(JsonObject envelope) =>
        envelope.TryGetPropertyValue(ModelVisibleToolResultConventions.Kind, out JsonNode? kindNode) &&
        kindNode is JsonValue kindValue &&
        kindValue.TryGetValue<string>(out string? kind) &&
        string.Equals(kind, ModelVisibleToolResultConventions.KindValue, StringComparison.Ordinal);

    private static string ToModelVisibleText(object? rawResult) => rawResult switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? string.Empty,
        JsonElement json => json.GetRawText(),
        _ => JsonSerializer.Serialize(rawResult),
    };
}
