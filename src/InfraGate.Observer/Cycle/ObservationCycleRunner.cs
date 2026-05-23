using System.Diagnostics;
using InfraGate.Observer.Classification;
using InfraGate.Observer.Mcp;
using InfraGate.Observer.Prompts;
using InfraGate.Observer.Snapshot;

namespace InfraGate.Observer.Cycle;

internal sealed class ObservationCycleRunner : IObservationCycleRunner
{
    private readonly IOptions<ObserverOptions> options;
    private readonly IOptionsMonitor<ObserverOptions> optionsMonitor;
    private readonly ISnapshotFetcher snapshotFetcher;
    private readonly ISystemPromptProvider systemPromptProvider;
    private readonly IChatClient chatClient;
    private readonly ISeverityClassifier severityClassifier;
    private readonly IObserverMcpClient mcpClient;
    private readonly ILogger<ObservationCycleRunner> logger;

    public ObservationCycleRunner(
        IOptions<ObserverOptions> options,
        IOptionsMonitor<ObserverOptions> optionsMonitor,
        ISnapshotFetcher snapshotFetcher,
        ISystemPromptProvider systemPromptProvider,
        IChatClient chatClient,
        ISeverityClassifier severityClassifier,
        IObserverMcpClient mcpClient,
        ILogger<ObservationCycleRunner> logger)
    {
        this.options = options;
        this.optionsMonitor = optionsMonitor;
        this.snapshotFetcher = snapshotFetcher;
        this.systemPromptProvider = systemPromptProvider;
        this.chatClient = chatClient;
        this.severityClassifier = severityClassifier;
        this.mcpClient = mcpClient;
        this.logger = logger;
    }

    public async Task<CycleResult> RunAsync(CancellationToken shutdownToken)
    {
        var cycleId = Guid.NewGuid().ToString("D");
        using var _ = logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["CycleId"] = cycleId });

        var opts = optionsMonitor.CurrentValue;
        var stopwatch = Stopwatch.StartNew();
        var allReports = new List<AnomalyReport>();
        var totalToolCalls = 0;
        var totalDisagreements = 0;
        var isTruncated = false;

        using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        cycleCts.CancelAfter(TimeSpan.FromSeconds(opts.WallClockCapSeconds));

        try
        {
            foreach (var ns in opts.AllowedNamespaces)
            {
                var (nsReports, toolCalls, disagreements, truncated) = await AnalyzeNamespaceAsync(
                    ns, cycleId, opts.MaxToolIterations, cycleCts.Token).ConfigureAwait(false);

                allReports.AddRange(nsReports);
                totalToolCalls += toolCalls;
                totalDisagreements += disagreements;

                if (truncated)
                {
                    isTruncated = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (shutdownToken.IsCancellationRequested)
            {
                logger.LogInformation("Observation cycle {CycleId} cancelled: host shutting down", cycleId);
            }
            else
            {
                logger.LogWarning("Observation cycle {CycleId} truncated: wall-clock cap reached", cycleId);
            }

            isTruncated = true;
        }

        stopwatch.Stop();

        if (isTruncated)
        {
            logger.LogWarning(
                "Cycle {CycleId} truncated — emitting no reports. ToolCalls={ToolCalls}",
                cycleId, totalToolCalls);

            return new CycleResult
            {
                CycleId = cycleId,
                Reports = Array.Empty<AnomalyReport>(),
                IsTruncated = true,
                ToolCallsUsed = totalToolCalls,
                SeverityDisagreements = totalDisagreements,
                Duration = stopwatch.Elapsed,
            };
        }

        logger.LogInformation(
            "Cycle {CycleId} complete. Reports={ReportCount} ToolCalls={ToolCalls} Disagreements={Disagreements} Duration={DurationMs}ms",
            cycleId, allReports.Count, totalToolCalls, totalDisagreements, (long)stopwatch.Elapsed.TotalMilliseconds);

        return new CycleResult
        {
            CycleId = cycleId,
            Reports = allReports,
            IsTruncated = false,
            ToolCallsUsed = totalToolCalls,
            SeverityDisagreements = totalDisagreements,
            Duration = stopwatch.Elapsed,
        };
    }

    private async Task<(List<AnomalyReport> Reports, int ToolCalls, int Disagreements, bool Truncated)> AnalyzeNamespaceAsync(
        string namespaceName,
        string cycleId,
        int maxToolIterations,
        CancellationToken cancellationToken)
    {
        var reports = new List<AnomalyReport>();
        var toolCallsUsed = 0;
        var disagreements = 0;

        var snapshot = await snapshotFetcher.FetchAsync(namespaceName, cancellationToken).ConfigureAwait(false);
        var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotSerializerOptions.Instance);

        var systemPrompt = systemPromptProvider.Get(namespaceName, maxToolIterations);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, snapshotJson),
        };

        string? llmResponseText = null;

        while (toolCallsUsed < maxToolIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var responseText = response.Text ?? string.Empty;

            var toolCall = TryParseToolCall(responseText);
            if (toolCall is not null)
            {
                toolCallsUsed++;
                messages.Add(new ChatMessage(ChatRole.Assistant, responseText));

                string toolResult;
                try
                {
                    toolResult = await mcpClient.GetToolResultAsync(
                        toolCall.Value.ToolName,
                        toolCall.Value.Arguments,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    toolResult = $"Error executing tool '{toolCall.Value.ToolName}': {ex.Message}";
                    logger.LogWarning(ex, "Tool call failed: {ToolName}", toolCall.Value.ToolName);
                }

                messages.Add(new ChatMessage(ChatRole.User, toolResult));
            }
            else
            {
                llmResponseText = responseText;
                break;
            }
        }

        if (toolCallsUsed >= maxToolIterations)
        {
            return (reports, toolCallsUsed, disagreements, Truncated: true);
        }

        if (string.IsNullOrEmpty(llmResponseText))
        {
            return (reports, toolCallsUsed, disagreements, Truncated: false);
        }

        var (parsedReports, parseDisagreements) = ParseLlmOutput(llmResponseText, cycleId, namespaceName);
        disagreements += parseDisagreements;
        reports.AddRange(parsedReports);

        return (reports, toolCallsUsed, disagreements, Truncated: false);
    }

    private (List<AnomalyReport> Reports, int Disagreements) ParseLlmOutput(
        string llmOutput,
        string cycleId,
        string namespaceName)
    {
        var reports = new List<AnomalyReport>();
        var disagreements = 0;

        var json = ExtractJsonArray(llmOutput);
        if (json is null)
        {
            logger.LogWarning("Failed to extract JSON array from LLM output for namespace {Namespace}", namespaceName);
            return (reports, disagreements);
        }

        List<LlmAnomalyOutput>? llmReports;
        try
        {
            llmReports = JsonSerializer.Deserialize<List<LlmAnomalyOutput>>(json, LlmOutputSerializerOptions.Instance);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse LLM output as JSON array for namespace {Namespace}", namespaceName);
            return (reports, disagreements);
        }

        if (llmReports is null)
        {
            return (reports, disagreements);
        }

        var detectedAt = DateTimeOffset.UtcNow;

        foreach (var llmReport in llmReports)
        {
            var kind = ParseAnomalyKind(llmReport.Kind);
            var llmSeverity = ParseSeverity(llmReport.Severity);
            var target = BuildResourceRef(llmReport.Target, namespaceName);
            if (target is null)
            {
                continue;
            }

            var evidence = BuildAnomalyEvidence(kind, target, llmReport);
            var (classifierSeverity, matchedRule) = severityClassifier.Classify(evidence);

            if (classifierSeverity != llmSeverity)
            {
                disagreements++;
                logger.LogInformation(
                    "Severity disagreement: LLM={LlmSeverity} Classifier={ClassifierSeverity} Rule={Rule} Kind={Kind} Target={Target}",
                    llmSeverity, classifierSeverity, matchedRule, kind, $"{target.Kind}/{target.Name}");
            }

            var anomalyId = AnomalyObserverConventions.ComputeAnomalyId(kind, target);

            var annotations = llmReport.Annotations ?? new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(matchedRule))
            {
                annotations["MatchedRule"] = matchedRule;
            }

            reports.Add(new AnomalyReport
            {
                AnomalyId = anomalyId,
                CycleId = cycleId,
                DetectedAt = detectedAt,
                Kind = kind,
                Target = target,
                Severity = classifierSeverity,
                Status = AnomalyStatus.Active,
                Summary = llmReport.Summary ?? string.Empty,
                Evidence = ParseEvidence(llmReport.Evidence),
                Suggested = ParseRemediationHint(llmReport.Suggested),
                Annotations = annotations,
            });
        }

        return (reports, disagreements);
    }

    private static AnomalyEvidence BuildAnomalyEvidence(AnomalyKind kind, ResourceRef target, LlmAnomalyOutput llmReport)
    {
        var annotations = llmReport.Annotations ?? new Dictionary<string, string>(StringComparer.Ordinal);

        return new AnomalyEvidence
        {
            Kind = kind,
            Target = target,
            PodCondition = annotations.GetValueOrDefault("PodCondition"),
            IsAllPodsAffected = ParseBoolAnnotation(annotations, "IsAllPodsAffected") ?? false,
            HasHealthySiblings = ParseBoolAnnotation(annotations, "HasHealthySiblings") ?? false,
            IsPending = ParseBoolAnnotation(annotations, "IsPending") ?? false,
            EndpointCount = ParseIntAnnotation(annotations, "EndpointCount"),
            SpecReplicas = ParseIntAnnotation(annotations, "ReplicasDesired"),
            AvailableReplicas = ParseIntAnnotation(annotations, "ReplicasAvailable"),
            IsSustained = ParseBoolAnnotation(annotations, "IsSustained") ?? false,
            EventType = annotations.GetValueOrDefault("EventType"),
            WarningCount = ParseIntAnnotation(annotations, "WarningCount") ?? 0,
            RestartCountSinceLastCycle = ParseIntAnnotation(annotations, "RestartCountSinceLastCycle"),
        };
    }

    private static string? ExtractJsonArray(string text)
    {
        var startIndex = text.IndexOf('[', StringComparison.Ordinal);
        var endIndex = text.LastIndexOf(']');

        if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
        {
            return null;
        }

        return text[startIndex..(endIndex + 1)];
    }

    private static (string ToolName, IReadOnlyDictionary<string, object?> Arguments)? TryParseToolCall(string text)
    {
        var prefixIndex = text.IndexOf("TOOL_CALL:", StringComparison.Ordinal);
        if (prefixIndex < 0)
        {
            return null;
        }

        var jsonStart = text.IndexOf('{', prefixIndex);
        if (jsonStart < 0)
        {
            return null;
        }

        var jsonEnd = text.LastIndexOf('}');
        if (jsonEnd < 0 || jsonEnd <= jsonStart)
        {
            return null;
        }

        var json = text[jsonStart..(jsonEnd + 1)];

        try
        {
            var toolCall = JsonSerializer.Deserialize<LlmToolCall>(json, LlmOutputSerializerOptions.Instance);
            if (toolCall is { Tool: not null })
            {
                var args = toolCall.Arguments ?? new Dictionary<string, object?>(StringComparer.Ordinal);
                return (toolCall.Tool, args);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static AnomalyKind ParseAnomalyKind(string? value)
    {
        return value switch
        {
            "PodUnhealthy" => AnomalyKind.PodUnhealthy,
            "DeploymentUnavailable" => AnomalyKind.DeploymentUnavailable,
            "ServiceNoEndpoints" => AnomalyKind.ServiceNoEndpoints,
            "WarningEvent" => AnomalyKind.WarningEvent,
            _ => AnomalyKind.WarningEvent,
        };
    }

    private static Severity ParseSeverity(string? value)
    {
        return value switch
        {
            "High" => Severity.High,
            "Medium" => Severity.Medium,
            "Low" => Severity.Low,
            _ => Severity.Low,
        };
    }

    private static ResourceRef? BuildResourceRef(LlmTargetOutput? target, string defaultNamespace)
    {
        if (target is null || string.IsNullOrWhiteSpace(target.Name))
        {
            return null;
        }

        return new ResourceRef
        {
            ApiVersion = target.ApiVersion ?? "v1",
            Kind = target.Kind ?? "Unknown",
            Namespace = target.Namespace ?? defaultNamespace,
            Name = target.Name,
        };
    }

    private static IReadOnlyList<EvidenceItem> ParseEvidence(List<LlmEvidenceOutput>? evidence)
    {
        if (evidence is null)
        {
            return Array.Empty<EvidenceItem>();
        }

        return evidence
            .Where(e => !string.IsNullOrWhiteSpace(e.Source) || !string.IsNullOrWhiteSpace(e.Content))
            .Select(e => new EvidenceItem
            {
                Source = e.Source ?? string.Empty,
                Content = e.Content ?? string.Empty,
                CapturedAt = ParseDateTimeOffset(e.CapturedAt),
            })
            .ToList();
    }

    private static RemediationHint? ParseRemediationHint(LlmRemediationOutput? suggested)
    {
        if (suggested is null)
        {
            return null;
        }

        return new RemediationHint
        {
            Action = suggested.Action,
            Explanation = suggested.Explanation,
        };
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result))
        {
            return result;
        }

        return DateTimeOffset.UtcNow;
    }

    private static bool? ParseBoolAnnotation(IReadOnlyDictionary<string, string> annotations, string key)
    {
        if (annotations.TryGetValue(key, out var value) && bool.TryParse(value, out var result))
        {
            return result;
        }

        return null;
    }

    private static int? ParseIntAnnotation(IReadOnlyDictionary<string, string> annotations, string key)
    {
        if (annotations.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    // ── LLM output DTOs ─────────────────────────────────────

    private sealed class LlmAnomalyOutput
    {
        public string? Kind { get; set; }
        public string? Severity { get; set; }
        public LlmTargetOutput? Target { get; set; }
        public string? Summary { get; set; }
        public List<LlmEvidenceOutput>? Evidence { get; set; }
        public LlmRemediationOutput? Suggested { get; set; }
        public Dictionary<string, string>? Annotations { get; set; }
    }

    private sealed class LlmTargetOutput
    {
        public string? ApiVersion { get; set; }
        public string? Kind { get; set; }
        public string? Namespace { get; set; }
        public string? Name { get; set; }
    }

    private sealed class LlmEvidenceOutput
    {
        public string? Source { get; set; }
        public string? Content { get; set; }
        public string? CapturedAt { get; set; }
    }

    private sealed class LlmRemediationOutput
    {
        public string? Action { get; set; }
        public string? Explanation { get; set; }
    }

    private sealed class LlmToolCall
    {
        public string? Tool { get; set; }
        public Dictionary<string, object?>? Arguments { get; set; }
    }
}
