using System.Text.Json;

namespace InfraGate.AgentGuardrails;

public static class ModelVisibleContentGuardExtensions
{
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

            return decision.Text;
        });
    }

    private static string ToModelVisibleText(object? rawResult) => rawResult switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? string.Empty,
        JsonElement json => json.GetRawText(),
        _ => JsonSerializer.Serialize(rawResult),
    };
}
