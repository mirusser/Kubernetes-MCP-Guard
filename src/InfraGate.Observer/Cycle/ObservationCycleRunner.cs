using System.Diagnostics;
using System.Diagnostics.Metrics;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Classification;
using InfraGate.Observer.Diagnostics;
using InfraGate.Observer.Handoff;
using InfraGate.Observer.Mcp;
using InfraGate.Observer.Prompts;
using InfraGate.Observer.Snapshot;
using InfraGate.Observer.State;
using Serilog.Context;

namespace InfraGate.Observer.Cycle;

internal sealed class ObservationCycleRunner : IObservationCycleRunner
{
    private readonly IOptionsMonitor<ObserverOptions> optionsMonitor;
    private readonly ISnapshotFetcher snapshotFetcher;
    private readonly ISystemPromptProvider systemPromptProvider;
    private readonly IChatClient chatClient;
    private readonly ISeverityClassifier severityClassifier;
    private readonly IObserverMcpClient mcpClient;
    private readonly IAnomalyDedupeStore dedupeStore;
    private readonly IAnomalyHandoffSink handoffSink;
    private readonly IObserverAuditOutbox? auditOutbox;
    private readonly ILogger<ObservationCycleRunner> logger;
    private readonly Counter<long>? cycleCountCounter;
    private readonly Counter<long>? toolCallsCounter;
    private readonly Counter<long>? severityDisagreementCounter;
    private readonly Counter<long>? reportsEmittedCounter;
    private readonly Histogram<double>? cycleDurationHistogram;

    public ObservationCycleRunner( // NOSONAR:S107 — DI constructor; all params are required services.
        IOptionsMonitor<ObserverOptions> optionsMonitor,
        ISnapshotFetcher snapshotFetcher,
        ISystemPromptProvider systemPromptProvider,
        IChatClient chatClient,
        ISeverityClassifier severityClassifier,
        IObserverMcpClient mcpClient,
        IAnomalyDedupeStore dedupeStore,
        IAnomalyHandoffSink handoffSink,
        ILogger<ObservationCycleRunner> logger,
        Meter? meter = null,
        IObserverAuditOutbox? auditOutbox = null)
    {
        this.optionsMonitor = optionsMonitor;
        this.snapshotFetcher = snapshotFetcher;
        this.systemPromptProvider = systemPromptProvider;
        this.chatClient = chatClient;
        this.severityClassifier = severityClassifier;
        this.mcpClient = mcpClient;
        this.dedupeStore = dedupeStore;
        this.handoffSink = handoffSink;
        this.auditOutbox = auditOutbox;
        this.logger = logger;

        cycleCountCounter = ObserverMetrics.CreateCycleCountCounter(meter);
        toolCallsCounter = ObserverMetrics.CreateToolCallsCounter(meter);
        severityDisagreementCounter = ObserverMetrics.CreateSeverityDisagreementCounter(meter);
        reportsEmittedCounter = ObserverMetrics.CreateReportsEmittedCounter(meter);
        cycleDurationHistogram = ObserverMetrics.CreateCycleDurationHistogram(meter);
    }

    public async Task<CycleResult> RunAsync(CancellationToken shutdownToken)
    {
        var cycleId = Guid.NewGuid().ToString("D");
        using var _ = LogContext.PushProperty("CycleId", cycleId);

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
            isTruncated = true;

            if (shutdownToken.IsCancellationRequested)
            {
                ObserverLogEvents.LogCycleCancelled(logger);
            }
            else
            {
                ObserverLogEvents.LogCycleTruncated(logger);
            }
        }

        stopwatch.Stop();
        toolCallsCounter?.Add(totalToolCalls);

        if (isTruncated)
        {
            ObserverLogEvents.LogTruncatedNoReports(logger, cycleId, totalToolCalls);

            cycleCountCounter?.Add(1,
                new KeyValuePair<string, object?>(ObserverMetrics.ResultTag, ObserverMetrics.ResultTruncated));

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

        return await CompleteCycleAsync(cycleId, allReports, totalToolCalls, totalDisagreements,
            stopwatch.Elapsed, shutdownToken).ConfigureAwait(false);
    }

    private async Task<CycleResult> CompleteCycleAsync(
        string cycleId,
        List<AnomalyReport> allReports,
        int totalToolCalls,
        int totalDisagreements,
        TimeSpan duration,
        CancellationToken shutdownToken)
    {
        var detectedAt = DateTimeOffset.UtcNow;
        var opts = optionsMonitor.CurrentValue;
        var (dedupedReports, resolvedReports, suppressedReports) = dedupeStore.ProcessReports(
            cycleId, allReports, opts.DedupeSuppressionWindow, opts.DedupeResolutionThreshold, detectedAt);

        var finalReports = new List<AnomalyReport>(dedupedReports.Count + resolvedReports.Count);
        finalReports.AddRange(dedupedReports);
        finalReports.AddRange(resolvedReports);

        if (finalReports.Count > 0)
        {
            var handoffBatch = new AnomalyHandoffBatch
            {
                CycleId = cycleId,
                EmittedAt = detectedAt,
                Reports = finalReports,
            };

            await handoffSink.PublishAsync(handoffBatch, shutdownToken).ConfigureAwait(false);
        }

        if (auditOutbox is not null)
        {
            await EmitAnomalyAuditEventsAsync(cycleId, dedupedReports, suppressedReports, resolvedReports, shutdownToken)
                .ConfigureAwait(false);
        }

        ObserverLogEvents.LogCycleCompletedDetailed(
            logger, cycleId, finalReports.Count, dedupedReports.Count, resolvedReports.Count,
            totalToolCalls, totalDisagreements, (long)duration.TotalMilliseconds);

        cycleCountCounter?.Add(1,
            new KeyValuePair<string, object?>(ObserverMetrics.ResultTag, ObserverMetrics.ResultCompleted));
        cycleDurationHistogram?.Record(duration.TotalMilliseconds);

        if (severityDisagreementCounter is not null && totalDisagreements > 0)
        {
            severityDisagreementCounter.Add(totalDisagreements);
        }

        if (reportsEmittedCounter is not null)
        {
            foreach (var report in finalReports)
            {
                var statusTag = report.Status switch
                {
                    AnomalyStatus.Active => "active",
                    AnomalyStatus.Resolved => "resolved",
                    _ => "unknown",
                };
                reportsEmittedCounter.Add(1,
                    new KeyValuePair<string, object?>(ObserverMetrics.StatusTag, statusTag));
            }
        }

        return new CycleResult
        {
            CycleId = cycleId,
            Reports = finalReports,
            IsTruncated = false,
            ToolCallsUsed = totalToolCalls,
            SeverityDisagreements = totalDisagreements,
            Duration = duration,
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
        var llmCallNumber = 0;

        while (toolCallsUsed < maxToolIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            llmCallNumber++;
            ObserverLogEvents.LogLlmCallStarting(logger, namespaceName, llmCallNumber);
            var sw = Stopwatch.StartNew();
            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            ObserverLogEvents.LogLlmCallCompleted(logger, namespaceName, llmCallNumber, sw.ElapsedMilliseconds);

            var responseText = response.Text ?? string.Empty;

            var toolCall = TryParseToolCall(logger, responseText);
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
                        cancellationToken).ConfigureAwait(false)
                        ?? $"Error: tool '{toolCall.Value.ToolName}' returned an error response.";
                }
                catch (Exception ex)
                {
                    toolResult = $"Error executing tool '{toolCall.Value.ToolName}': {ex.Message}";
                    ObserverLogEvents.LogToolCallFailed(logger, toolCall.Value.ToolName, ex);
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
            ObserverLogEvents.LogJsonArrayExtractFailed(logger, namespaceName);
            return (reports, disagreements);
        }

        List<LlmAnomalyOutput>? llmReports;
        try
        {
            llmReports = JsonSerializer.Deserialize<List<LlmAnomalyOutput>>(json, LlmOutputSerializerOptions.Instance);
        }
        catch (JsonException ex)
        {
            ObserverLogEvents.LogJsonParseFailed(logger, namespaceName, ex);
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

            var annotations = new Dictionary<string, string>(StringComparer.Ordinal);
            if (llmReport.Annotations is { ValueKind: JsonValueKind.Object } obj)
            {
                foreach (var prop in obj.EnumerateObject())
                {
                    annotations[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => prop.Value.GetRawText()
                    };
                }
            }

            var evidence = BuildAnomalyEvidence(kind, target, annotations);
            var (classifierSeverity, matchedRule) = severityClassifier.Classify(evidence);

            if (classifierSeverity != llmSeverity)
            {
                disagreements++;
                ObserverLogEvents.LogSeverityDisagreement(
                    logger, llmSeverity.ToString(), classifierSeverity.ToString(), matchedRule, kind.ToString(), $"{target.Kind}/{target.Name}");
            }

            var anomalyId = AnomalyObserverConventions.ComputeAnomalyId(kind, target);
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

    private static AnomalyEvidence BuildAnomalyEvidence(AnomalyKind kind, ResourceRef target, IReadOnlyDictionary<string, string> annotations)
    {

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
        if (startIndex < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '[' || c == '{') depth++;
            else if (c == ']' || c == '}') depth--;

            if (depth == 0)
            {
                return text[startIndex..(i + 1)];
            }
        }

        return null;
    }

    private static (string ToolName, IReadOnlyDictionary<string, object?> Arguments)? TryParseToolCall(ILogger logger, string text)
    {
        var jsonStart = text.IndexOf('{', StringComparison.Ordinal);
        if (jsonStart < 0) return null;

        var jsonEnd = text.LastIndexOf('}', StringComparison.Ordinal);
        if (jsonEnd < 0 || jsonEnd <= jsonStart) return null;

        var json = text[jsonStart..(jsonEnd + 1)];

        try
        {
            var toolCall = JsonSerializer.Deserialize<LlmToolCall>(json, LlmOutputSerializerOptions.Instance);
            if (!string.IsNullOrWhiteSpace(toolCall?.Tool))
            {
                var args = toolCall.Arguments ?? toolCall.Parameters ?? new Dictionary<string, object?>(StringComparer.Ordinal);
                return (toolCall.Tool, args);
            }
        }
        catch (JsonException ex)
        {
            ObserverLogEvents.LogLlmNonJsonToolCall(logger, ex);
            // Benign — LLM sometimes returns non-JSON tool calls; skip and continue.
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

    private async Task EmitAnomalyAuditEventsAsync(
        string cycleId,
        IReadOnlyList<AnomalyReport> detectedReports,
        IReadOnlyList<AnomalyReport> suppressedReports,
        IReadOnlyList<AnomalyReport> resolvedReports,
        CancellationToken cancellationToken)
    {
        foreach (var report in detectedReports)
        {
            await auditOutbox!.AppendAsync(new ObserverAuditEntry(
                EventName: ObserverAuditEvents.AnomalyDetected,
                Payload: new
                {
                    report.AnomalyId,
                    kind = report.Kind.ToString("G"),
                    severity = report.Severity.ToString("G"),
                    target = $"{report.Target.Kind}/{report.Target.Namespace}/{report.Target.Name}",
                    report.Summary,
                },
                ActorSubject: "service:observer",
                CycleId: cycleId,
                AnomalyId: report.AnomalyId,
                DedupeKey: DedupeKeyString(report),
                Outcome: report.Status == AnomalyStatus.Resolved ? "resolved" : "active"),
            cancellationToken).ConfigureAwait(false);
        }

        foreach (var report in suppressedReports)
        {
            await auditOutbox!.AppendAsync(new ObserverAuditEntry(
                EventName: ObserverAuditEvents.AnomalySuppressed,
                Payload: new
                {
                    report.AnomalyId,
                    kind = report.Kind.ToString("G"),
                    severity = report.Severity.ToString("G"),
                    target = $"{report.Target.Kind}/{report.Target.Namespace}/{report.Target.Name}",
                },
                ActorSubject: "service:observer",
                CycleId: cycleId,
                AnomalyId: report.AnomalyId,
                DedupeKey: DedupeKeyString(report),
                Outcome: "suppressed"),
            cancellationToken).ConfigureAwait(false);
        }

        foreach (var report in resolvedReports)
        {
            await auditOutbox!.AppendAsync(new ObserverAuditEntry(
                EventName: ObserverAuditEvents.AnomalyResolved,
                Payload: new
                {
                    report.AnomalyId,
                    kind = report.Kind.ToString("G"),
                    target = $"{report.Target.Kind}/{report.Target.Namespace}/{report.Target.Name}",
                },
                ActorSubject: "service:observer",
                CycleId: cycleId,
                AnomalyId: report.AnomalyId,
                DedupeKey: DedupeKeyString(report),
                Outcome: "resolved"),
            cancellationToken).ConfigureAwait(false);
        }
    }

    private static string DedupeKeyString(AnomalyReport report) =>
        $"{report.Kind:G}/{report.Target.Kind}/{report.Target.Namespace}/{report.Target.Name}";

    // ── LLM output DTOs ─────────────────────────────────────

    // JSON deserialization DTOs; properties set by System.Text.Json at runtime.
#pragma warning disable S1144, S3459
    private sealed class LlmAnomalyOutput
    {
        public string? Kind { get; set; }
        public string? Severity { get; set; }
        public LlmTargetOutput? Target { get; set; }
        public string? Summary { get; set; }
        public List<LlmEvidenceOutput>? Evidence { get; set; }
        public LlmRemediationOutput? Suggested { get; set; }
        public JsonElement? Annotations { get; set; }
    }

    // JSON deserialization DTOs.
    private sealed class LlmTargetOutput
    {
        public string? ApiVersion { get; set; }
        public string? Kind { get; set; }
        public string? Namespace { get; set; }
        public string? Name { get; set; }
    }

    // JSON deserialization DTOs.
    private sealed class LlmEvidenceOutput
    {
        public string? Source { get; set; }
        public string? Content { get; set; }
        public string? CapturedAt { get; set; }
    }

    // JSON deserialization DTOs.
    private sealed class LlmRemediationOutput
    {
        public string? Action { get; set; }
        public string? Explanation { get; set; }
    }

    // JSON deserialization DTOs.
    private sealed class LlmToolCall
    {
        public string? Tool { get; set; }
        public Dictionary<string, object?>? Arguments { get; set; }
        public Dictionary<string, object?>? Parameters { get; set; }
    }
#pragma warning restore S1144, S3459
}
