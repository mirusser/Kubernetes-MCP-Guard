using InfraGate.Observer.Classification;
using InfraGate.Observer.Diagnostics;
using Microsoft.Agents.AI.Workflows;

namespace InfraGate.Observer.Cycle.Workflow;

internal sealed class AnomalyParseExecutor(
    string id,
    string namespaceName,
    string cycleId,
    Func<int> getToolCallCount,
    ISeverityClassifier severityClassifier,
    ILogger logger) : Executor<List<ChatMessage>, NamespaceParseResult>(id)
{
    public override ValueTask<NamespaceParseResult> HandleAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var toolCallsUsed = getToolCallCount();

        var llmText = messages
            .LastOrDefault(m => m.Role == ChatRole.Assistant)
            ?.Text ?? string.Empty;

        var (reports, disagreements) = ParseLlmOutput(llmText);
        return ValueTask.FromResult(new NamespaceParseResult(
            NamespaceName: namespaceName,
            CycleId: cycleId,
            Reports: reports,
            ToolCallsUsed: toolCallsUsed,
            SeverityDisagreements: disagreements));
    }

    private (List<AnomalyReport> Reports, int Disagreements) ParseLlmOutput(string llmOutput)
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

        if (llmReports is null) return (reports, disagreements);

        var detectedAt = DateTimeOffset.UtcNow;
        foreach (var llmReport in llmReports)
        {
            var kind = ParseAnomalyKind(llmReport.Kind);
            var llmSeverity = ParseSeverity(llmReport.Severity);
            var target = BuildResourceRef(llmReport.Target, namespaceName);
            if (target is null) continue;

            var annotations = BuildAnnotations(llmReport.Annotations);
            var evidence = BuildAnomalyEvidence(kind, target, annotations);
            var (classifierSeverity, matchedRule) = severityClassifier.Classify(evidence);

            if (classifierSeverity != llmSeverity)
            {
                disagreements++;
                ObserverLogEvents.LogSeverityDisagreement(
                    logger, llmSeverity.ToString(), classifierSeverity.ToString(), matchedRule,
                    kind.ToString(), $"{target.Kind}/{target.Name}");
            }

            var anomalyId = AnomalyObserverConventions.ComputeAnomalyId(kind, target);
            if (!string.IsNullOrEmpty(matchedRule))
                annotations["MatchedRule"] = matchedRule;

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

    private static Dictionary<string, string> BuildAnnotations(JsonElement? annotationsElement)
    {
        var annotations = new Dictionary<string, string>(StringComparer.Ordinal);
        if (annotationsElement is { ValueKind: JsonValueKind.Object } obj)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                annotations[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => prop.Value.GetRawText(),
                };
            }
        }
        return annotations;
    }

    private static AnomalyEvidence BuildAnomalyEvidence(
        AnomalyKind kind, ResourceRef target, IReadOnlyDictionary<string, string> annotations) =>
        new()
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

    private static string? ExtractJsonArray(string text)
    {
        var startIndex = text.IndexOf('[', StringComparison.Ordinal);
        if (startIndex < 0) return null;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (escape) { escape = false; continue; }
            if (c == '\\' && inString) { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '[' || c == '{') depth++;
            else if (c == ']' || c == '}') depth--;

            if (depth == 0) return text[startIndex..(i + 1)];
        }

        return null;
    }

    private static AnomalyKind ParseAnomalyKind(string? value) => value switch
    {
        "PodUnhealthy" => AnomalyKind.PodUnhealthy,
        "DeploymentUnavailable" => AnomalyKind.DeploymentUnavailable,
        "ServiceNoEndpoints" => AnomalyKind.ServiceNoEndpoints,
        "WarningEvent" => AnomalyKind.WarningEvent,
        _ => AnomalyKind.WarningEvent,
    };

    private static Severity ParseSeverity(string? value) => value switch
    {
        "High" => Severity.High,
        "Medium" => Severity.Medium,
        "Low" => Severity.Low,
        _ => Severity.Low,
    };

    private static ResourceRef? BuildResourceRef(LlmTargetOutput? target, string defaultNamespace)
    {
        if (target is null || string.IsNullOrWhiteSpace(target.Name)) return null;
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
        if (evidence is null) return Array.Empty<EvidenceItem>();
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

    private static RemediationHint? ParseRemediationHint(LlmRemediationOutput? suggested) =>
        suggested is null ? null : new RemediationHint
        {
            Action = suggested.Action,
            Explanation = suggested.Explanation,
        };

    private static DateTimeOffset ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result
            : DateTimeOffset.UtcNow;

    private static bool? ParseBoolAnnotation(IReadOnlyDictionary<string, string> a, string key) =>
        a.TryGetValue(key, out var v) && bool.TryParse(v, out var r) ? r : null;

    private static int? ParseIntAnnotation(IReadOnlyDictionary<string, string> a, string key) =>
        a.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : null;

    // ── LLM output DTOs ──────────────────────────────────────────────────────────────────────────
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
#pragma warning restore S1144, S3459
}
