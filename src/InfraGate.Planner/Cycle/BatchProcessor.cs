using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text;
using InfraGate.Planner.Decision;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InfraGate.Planner.Cycle;

internal sealed class BatchProcessor : BackgroundService
{
    private static readonly JsonSerializerOptions AnomalyJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOptionsMonitor<PlannerOptions> optionsMonitor;
    private readonly AnomalyBatchQueue queue;
    private readonly IChatClient chatClient;
    private readonly IPlannerMcpClient mcpClient;
    private readonly IRemediationProposalSink proposalSink;
    private readonly ILogger<BatchProcessor> logger;
    private readonly PlannerDedupeStore dedupeStore;
    private readonly Lazy<string> systemPrompt;
    private readonly Counter<long>? invalidOperationCounter;
    private readonly Counter<long>? invalidArgumentsCounter;
    private readonly Counter<long>? timeoutCounter;
    private readonly Counter<long>? proposeFailedCounter;

    public BatchProcessor( // NOSONAR:S107 — orchestrator dependencies are explicit production seams.
        IOptionsMonitor<PlannerOptions> optionsMonitor,
        AnomalyBatchQueue queue,
        IChatClient chatClient,
        IPlannerMcpClient mcpClient,
        IRemediationProposalSink proposalSink,
        ILogger<BatchProcessor> logger,
        PlannerDedupeStore? dedupeStore = null,
        Meter? meter = null)
    {
        this.optionsMonitor = optionsMonitor;
        this.queue = queue;
        this.chatClient = chatClient;
        this.mcpClient = mcpClient;
        this.proposalSink = proposalSink;
        this.logger = logger;
        this.dedupeStore = dedupeStore ?? new PlannerDedupeStore();
        systemPrompt = new Lazy<string>(LoadSystemPrompt);
        invalidOperationCounter = PlannerMetrics.CreateDecisionInvalidOperationCounter(meter);
        invalidArgumentsCounter = PlannerMetrics.CreateDecisionInvalidArgumentsCounter(meter);
        timeoutCounter = PlannerMetrics.CreateDecisionTimeoutCounter(meter);
        proposeFailedCounter = PlannerMetrics.CreateProposeFailedCounter(meter);
    }

    internal async Task ProcessBatchAsync(AnomalyHandoffBatch batch, CancellationToken shutdownToken)
    {
        var opts = optionsMonitor.CurrentValue;
        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        batchCts.CancelAfter(TimeSpan.FromSeconds(opts.BatchWallClockCapSeconds));

        var proposals = new List<RemediationProposal>();

        try
        {
            foreach (var report in batch.Reports)
            {
                batchCts.Token.ThrowIfCancellationRequested();

                if (!ShouldProcess(report))
                {
                    continue;
                }

                if (dedupeStore.HasActivePlan(report.AnomalyId))
                {
                    continue;
                }

                var decision = await DecideAsync(report, opts, batchCts.Token).ConfigureAwait(false);
                if (decision is null)
                {
                    continue;
                }

                var planId = await ProposePlanAsync(report, decision, batchCts.Token).ConfigureAwait(false);
                if (planId is null)
                {
                    continue;
                }

                var proposedAt = DateTimeOffset.UtcNow;
                dedupeStore.TrackActivePlan(report.AnomalyId, planId, proposedAt);
                proposals.Add(new RemediationProposal
                {
                    PlanId = planId,
                    AnomalyId = report.AnomalyId,
                    ProposedAt = proposedAt,
                });
            }
        }
        catch (OperationCanceledException) when (!shutdownToken.IsCancellationRequested)
        {
            // Batch cap reached. Publish proposals already produced in this batch.
        }

        if (proposals.Count > 0)
        {
            await proposalSink.PublishAsync(
                new RemediationProposalBatch
                {
                    CycleId = batch.CycleId,
                    EmittedAt = DateTimeOffset.UtcNow,
                    Proposals = proposals,
                },
                shutdownToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
        {
            while (queue.Reader.TryRead(out var batch))
            {
                try
                {
                    await ProcessBatchAsync(batch, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PlannerLogEvents.LogBatchProcessingFailed(logger, batch.CycleId, ex);
                }
            }
        }
    }

    private bool ShouldProcess(AnomalyReport report)
    {
        if (report.Status == AnomalyStatus.Resolved)
        {
            dedupeStore.Remove(report.AnomalyId);
            return false;
        }

        return report.Kind is AnomalyKind.PodUnhealthy
            or AnomalyKind.DeploymentUnavailable
            or AnomalyKind.ServiceNoEndpoints
            or AnomalyKind.WarningEvent;
    }

    private async Task<RemediationDecision?> DecideAsync(
        AnomalyReport report,
        PlannerOptions opts,
        CancellationToken batchToken)
    {
        using var anomalyCts = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
        anomalyCts.CancelAfter(TimeSpan.FromSeconds(opts.AnomalyWallClockCapSeconds));

        try
        {
            return await DecideCoreAsync(report, opts.MaxToolIterations, anomalyCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!batchToken.IsCancellationRequested)
        {
            timeoutCounter?.Add(1);
            PlannerLogEvents.LogDecisionTimedOut(logger, report.AnomalyId);
            return null;
        }
    }

    private async Task<RemediationDecision?> DecideCoreAsync(
        AnomalyReport report,
        int maxToolIterations,
        CancellationToken cancellationToken)
    {
        var anomalyJson = JsonSerializer.Serialize(report, AnomalyJsonOptions);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt.Value),
            new(ChatRole.User, anomalyJson),
        };

        string? responseText = null;
        int toolCallsUsed = 0;

        while (toolCallsUsed < maxToolIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            responseText = response.Text ?? string.Empty;

            var toolCall = TryParseToolCall(responseText);
            if (toolCall is null)
            {
                break;
            }

            toolCallsUsed++;
            messages.Add(new ChatMessage(ChatRole.Assistant, responseText));

            string toolResult;
            if (!PlannerConventions.ToolNames.ReadOnlyToolNames.Contains(toolCall.Value.ToolName))
            {
                toolResult = $"Error executing tool '{toolCall.Value.ToolName}': tool is not allowed for Planner LLM inspection.";
            }
            else
            {
                toolResult = await mcpClient.CallToolAsync(
                    toolCall.Value.ToolName,
                    toolCall.Value.Arguments,
                    cancellationToken).ConfigureAwait(false);
            }

            messages.Add(new ChatMessage(ChatRole.User, toolResult));
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        return ParseDecision(responseText, report.AnomalyId);
    }

    private RemediationDecision? ParseDecision(string responseText, string anomalyId)
    {
        var json = ExtractJsonObject(responseText);
        if (json is null)
        {
            invalidArgumentsCounter?.Add(1);
            PlannerLogEvents.LogDecisionInvalidArguments(logger, anomalyId, string.Empty);
            return null;
        }

        LlmDecisionOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<LlmDecisionOutput>(json, PlannerLlmSerializerOptions.Instance);
        }
        catch (JsonException)
        {
            invalidArgumentsCounter?.Add(1);
            PlannerLogEvents.LogDecisionInvalidArguments(logger, anomalyId, string.Empty);
            return null;
        }

        if (output is null || string.IsNullOrWhiteSpace(output.OperationType))
        {
            invalidArgumentsCounter?.Add(1);
            PlannerLogEvents.LogDecisionInvalidArguments(logger, anomalyId, string.Empty);
            return null;
        }

        if (!PlannerConventions.OperationTypes.AllowedOperationTypes.Contains(output.OperationType))
        {
            invalidOperationCounter?.Add(1);
            PlannerLogEvents.LogDecisionInvalidOperation(logger, anomalyId, output.OperationType);
            return null;
        }

        var decision = new RemediationDecision(
            output.OperationType,
            ConvertArguments(output.Arguments),
            output.Reasoning);

        if (!OperationArgumentValidator.TryNormalize(decision, out var normalizedArguments))
        {
            invalidArgumentsCounter?.Add(1);
            PlannerLogEvents.LogDecisionInvalidArguments(logger, anomalyId, decision.OperationType);
            return null;
        }

        return decision with { Arguments = normalizedArguments };
    }

    private async Task<string?> ProposePlanAsync(
        AnomalyReport report,
        RemediationDecision decision,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [PlannerConventions.ToolArguments.OperationType] = decision.OperationType,
                    [PlannerConventions.ToolArguments.OperationArguments] = decision.Arguments,
                },
                cancellationToken).ConfigureAwait(false);

            if (TryExtractPlanId(result, out var planId))
            {
                return planId;
            }

            proposeFailedCounter?.Add(1);
            PlannerLogEvents.LogProposePlanMissingPlanId(logger, report.AnomalyId);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            proposeFailedCounter?.Add(1);
            PlannerLogEvents.LogProposePlanFailed(logger, report.AnomalyId, ex);
            return null;
        }
    }

    private static IReadOnlyDictionary<string, object?> ConvertArguments(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var converted = new Dictionary<string, object?>(arguments.Count, StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            converted[key] = JsonElementToObject(value);
        }

        return converted;
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out int intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out long longValue) => longValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.Clone(),
        };
    }

    private static string? ExtractJsonObject(string text)
    {
        var startIndex = text.IndexOf('{', StringComparison.Ordinal);
        var endIndex = text.LastIndexOf('}');

        if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
        {
            return null;
        }

        return text[startIndex..(endIndex + 1)];
    }

    private static (string ToolName, IReadOnlyDictionary<string, object?> Arguments)? TryParseToolCall(string text)
    {
        var prefixIndex = text.IndexOf(PlannerConventions.Llm.ToolCallPrefix, StringComparison.Ordinal);
        if (prefixIndex < 0)
        {
            return null;
        }

        var json = ExtractJsonObject(text[prefixIndex..]);
        if (json is null)
        {
            return null;
        }

        try
        {
            var toolCall = JsonSerializer.Deserialize<LlmToolCall>(json, PlannerLlmSerializerOptions.Instance);
            if (toolCall is { Tool: not null })
            {
                return (toolCall.Tool, ConvertArguments(toolCall.Arguments));
            }
        }
        catch (JsonException)
        {
            // Benign LLM formatting error. Treat it as normal response text.
        }

        return null;
    }

    private static bool TryExtractPlanId(string response, out string planId)
    {
        planId = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(response);
            return TryExtractPlanId(document.RootElement, out planId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractPlanId(JsonElement element, out string planId)
    {
        planId = string.Empty;

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(PlannerConventions.ProposePlanResponseFields.PlanId, out var planIdElement) &&
            planIdElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(planIdElement.GetString()))
        {
            planId = planIdElement.GetString()!;
            return true;
        }

        if (TryExtractPlanIdFromText(element, PlannerConventions.ProposePlanResponseFields.TextLower, out planId) ||
            TryExtractPlanIdFromText(element, PlannerConventions.ProposePlanResponseFields.TextUpper, out planId))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.NameEquals(PlannerConventions.ProposePlanResponseFields.ContentLower) ||
                    property.NameEquals(PlannerConventions.ProposePlanResponseFields.ContentUpper)) &&
                    TryExtractPlanId(property.Value, out planId))
                {
                    return true;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryExtractPlanId(item, out planId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryExtractPlanIdFromText(JsonElement element, string propertyName, out string planId)
    {
        planId = string.Empty;

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var textElement) ||
            textElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = textElement.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return TryExtractPlanId(text, out planId);
    }

    private static string LoadSystemPrompt()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(PlannerConventions.Prompts.SystemPromptResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{PlannerConventions.Prompts.SystemPromptResourceName}' not found. Ensure PlannerSystemPrompt.md is an EmbeddedResource in the csproj.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // JSON deserialization DTOs; properties set by System.Text.Json at runtime.
#pragma warning disable S1144, S3459
    private sealed record class LlmDecisionOutput
    {
        public string? OperationType { get; set; }
        public Dictionary<string, JsonElement>? Arguments { get; set; }
        public string? Reasoning { get; set; }
    }

    private sealed record class LlmToolCall
    {
        public string? Tool { get; set; }
        public Dictionary<string, JsonElement>? Arguments { get; set; }
    }
#pragma warning restore S1144, S3459
}
