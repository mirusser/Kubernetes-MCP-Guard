using System.Diagnostics.Metrics;
using System.Text.Json.Serialization;
using InfraGate.AgentLlm;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Decision;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace InfraGate.Planner.Cycle.Workflow;

[SendsMessage(typeof(DecisionContext))]
internal sealed class DecideExecutor( // NOSONAR:S107 — DI constructor; all params are required services.
    string id,
    ToolCallingAgentFactory agentFactory,
    string systemPrompt,
    IReadOnlyList<AITool> tools,
    int maxToolIterations,
    int anomalyWallClockCapSeconds,
    Counter<long>? timeoutCounter,
    ILogger logger,
    AgentGuardrailPolicy? guardrailPolicy = null,
    PlannerDedupeStore? dedupeStore = null,
    IPlannerAuditOutbox? auditOutbox = null,
    IModelVisibleContentGuard? contentGuard = null) : Executor<AnomalyReport>(id)
{
    private static readonly JsonSerializerOptions anomalyJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ChatResponseFormat decisionResponseFormat =
        ChatResponseFormat.ForJsonSchema<LlmDecisionOutput>();

    public override async ValueTask HandleAsync(
        AnomalyReport message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var (decision, timedOut) = await DecideWithTimeoutAsync(message, cancellationToken).ConfigureAwait(false);
        if (decision is null)
        {
            var now = DateTimeOffset.UtcNow;
            dedupeStore?.TrackActivePlan(message.AnomalyId, string.Empty, now,
                now + PlannerConventions.Dedupe.FailedProposalBackoff);

            if (timedOut)
            {
                if (auditOutbox is not null)
                    await auditOutbox.AppendAsync(
                        new PlannerAuditEntry(
                            EventName: PlannerAuditEvents.DecisionTimedOut,
                            Payload: new { wallClockCapSeconds = anomalyWallClockCapSeconds },
                            AnomalyId: message.AnomalyId,
                            ActorSubject: PlannerConventions.Audit.ServicePlannerSubject,
                            Outcome: PlannerConventions.Audit.Outcomes.Failed,
                            Reason: "timeout"),
                        cancellationToken).ConfigureAwait(false);
            }
            else
            {
                PlannerLogEvents.LogDecisionNoOutput(logger, message.AnomalyId);
                if (auditOutbox is not null)
                    await auditOutbox.AppendAsync(
                        new PlannerAuditEntry(
                            EventName: PlannerAuditEvents.DecisionNoOutput,
                            Payload: new { },
                            AnomalyId: message.AnomalyId,
                            ActorSubject: PlannerConventions.Audit.ServicePlannerSubject,
                            Outcome: PlannerConventions.Audit.Outcomes.Failed,
                            Reason: "no_output"),
                        cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        await context.SendMessageAsync(new DecisionContext(message, decision), cancellationToken).ConfigureAwait(false);
    }

    private async Task<(RemediationDecision? Decision, bool TimedOut)> DecideWithTimeoutAsync(AnomalyReport message, CancellationToken batchToken)
    {
        using var anomalyCts = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
        anomalyCts.CancelAfter(TimeSpan.FromSeconds(anomalyWallClockCapSeconds));

        try
        {
            var decision = await DecideCoreAsync(message, anomalyCts.Token).ConfigureAwait(false);
            return (decision, false);
        }
        catch (OperationCanceledException) when (!batchToken.IsCancellationRequested)
        {
            timeoutCounter?.Add(1);
            PlannerLogEvents.LogDecisionTimedOut(logger, message.AnomalyId);
            return (null, true);
        }
    }

    private async Task<RemediationDecision?> DecideCoreAsync(AnomalyReport message, CancellationToken cancellationToken)
    {
        string anomalyJson = JsonSerializer.Serialize(message, anomalyJsonOptions);
        string agentId = $"{PlannerConventions.A2AHandoff.AgentIdPrefix}{message.AnomalyId[..Math.Min(8, message.AnomalyId.Length)]}";

        var guardContent = new ModelVisibleContent(
            anomalyJson,
            ModelVisibleContentSource.PlannerAnomaly,
            agentId);

        var guard = contentGuard ?? AllowAllModelVisibleContentGuard.Instance;
        var guardDecision = await guard.EvaluateAsync(guardContent, cancellationToken).ConfigureAwait(false);

        if (guardDecision.Action == ModelVisibleContentAction.BlockModelIngestion)
            return null;

        (AIAgent agent, _) = agentFactory.Create(agentId, systemPrompt, tools,
            maxToolIterations, decisionResponseFormat, guardrailPolicy);

        AgentResponse response = await agent.RunAsync(guardDecision.Text, cancellationToken: cancellationToken).ConfigureAwait(false);
        string responseText = response.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(responseText)) return null;
        return ParseDecision(message, responseText);
    }

    private RemediationDecision? ParseDecision(AnomalyReport message, string responseText)
    {
        logger.LogDebug("LLM raw response for anomaly {AnomalyId}: {ResponseText}", message.AnomalyId, responseText);

        string? json = ExtractJsonObject(responseText);
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

    private sealed record class LlmDecisionOutput
    {
        public string? OperationType { get; }
        public Dictionary<string, JsonElement>? Arguments { get; }
        public string? Reasoning { get; }

        [JsonConstructor]
        public LlmDecisionOutput(
            string? operationType,
            Dictionary<string, JsonElement>? arguments,
            string? reasoning)
        {
            OperationType = operationType;
            Arguments = arguments;
            Reasoning = reasoning;
        }
    }
}