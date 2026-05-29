using System.Diagnostics.Metrics;
using InfraGate.AgentLlm;
using InfraGate.Planner.Decision;
using InfraGate.Planner.Diagnostics;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace InfraGate.Planner.Cycle.Workflow;

[SendsMessage(typeof(DecisionContext))]
internal sealed class DecideExecutor(
    string id,
    ToolCallingAgentFactory agentFactory,
    string systemPrompt,
    IReadOnlyList<AITool> tools,
    int maxToolIterations,
    int anomalyWallClockCapSeconds,
    Counter<long>? timeoutCounter,
    ILogger logger) : Executor<AnomalyReport>(id)
{
    private static readonly JsonSerializerOptions AnomalyJsonOptions = new(JsonSerializerDefaults.Web);

    public override async ValueTask HandleAsync(
        AnomalyReport message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var decision = await DecideWithTimeoutAsync(message, cancellationToken).ConfigureAwait(false);
        if (decision is null) return;

        await context.SendMessageAsync(new DecisionContext(message, decision), cancellationToken).ConfigureAwait(false);
    }

    private async Task<RemediationDecision?> DecideWithTimeoutAsync(AnomalyReport message, CancellationToken batchToken)
    {
        using var anomalyCts = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
        anomalyCts.CancelAfter(TimeSpan.FromSeconds(anomalyWallClockCapSeconds));

        try
        {
            return await DecideCoreAsync(message, anomalyCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!batchToken.IsCancellationRequested)
        {
            timeoutCounter?.Add(1);
            PlannerLogEvents.LogDecisionTimedOut(logger, message.AnomalyId);
            return null;
        }
    }

    private async Task<RemediationDecision?> DecideCoreAsync(AnomalyReport message, CancellationToken cancellationToken)
    {
        var anomalyJson = JsonSerializer.Serialize(message, AnomalyJsonOptions);
        var (agent, _) = agentFactory.Create($"planner-{message.AnomalyId[..8]}", systemPrompt, tools, maxToolIterations);

        var response = await agent.RunAsync(anomalyJson, cancellationToken: cancellationToken).ConfigureAwait(false);
        var responseText = response.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(responseText)) return null;
        return ParseDecision(message, responseText);
    }

    private RemediationDecision? ParseDecision(AnomalyReport message, string responseText)
    {
        logger.LogDebug("LLM raw response for anomaly {AnomalyId}: {ResponseText}", message.AnomalyId, responseText);

        var json = ExtractJsonObject(responseText);
        if (json is null) return null;

        LlmDecisionOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<LlmDecisionOutput>(json, PlannerLlmSerializerOptions.Instance);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "LLM JSON parse failed for anomaly {AnomalyId}: {Json}", message.AnomalyId, json);
            return null;
        }

        if (output is null || string.IsNullOrWhiteSpace(output.OperationType)) return null;

        return new RemediationDecision(output.OperationType, ConvertArguments(output.Arguments), output.Reasoning);
    }

    private static string? ExtractJsonObject(string text)
    {
        var startIndex = text.IndexOf('{', StringComparison.Ordinal);
        var endIndex = text.LastIndexOf('}');
        if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex) return null;
        return text[startIndex..(endIndex + 1)];
    }

    private static IReadOnlyDictionary<string, object?> ConvertArguments(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments is null) return new Dictionary<string, object?>(StringComparer.Ordinal);
        var converted = new Dictionary<string, object?>(arguments.Count, StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
            converted[key] = JsonElementToObject(value);
        return converted;
    }

    private static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt32(out int i) => i,
        JsonValueKind.Number when element.TryGetInt64(out long l) => l,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.Clone(),
    };

#pragma warning disable S1144, S3459
    private sealed record class LlmDecisionOutput
    {
        public string? OperationType { get; set; }
        public Dictionary<string, JsonElement>? Arguments { get; set; }
        public string? Reasoning { get; set; }
    }
#pragma warning restore S1144, S3459
}
