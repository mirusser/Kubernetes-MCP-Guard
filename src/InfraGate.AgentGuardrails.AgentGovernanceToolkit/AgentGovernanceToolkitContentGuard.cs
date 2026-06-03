namespace InfraGate.AgentGuardrails.AgentGovernanceToolkit;

public sealed class AgentGovernanceToolkitContentGuard(PromptInjectionDetector detector)
    : IModelVisibleContentGuard
{
    // Bounded replacement for Redact decisions: does not include original content.
    private const string RedactedPlaceholder =
        "[CONTENT REDACTED: potential injection pattern detected by deterministic filter]";

    public Task<ModelVisibleContentDecision> EvaluateAsync(
        ModelVisibleContent content, CancellationToken cancellationToken)
    {
        var result = detector.Detect(content.Text);
        var (action, text) = MapThreatLevel(result.ThreatLevel, content.Text);
        return Task.FromResult(new ModelVisibleContentDecision(
            action,
            text,
            BuildCategories(result),
            BuildReason(result)));
    }

    private static (ModelVisibleContentAction action, string text) MapThreatLevel(
        ThreatLevel level, string originalText) =>
        level switch
        {
            ThreatLevel.None => (ModelVisibleContentAction.Allow, originalText),
            ThreatLevel.Low => (ModelVisibleContentAction.Redact, RedactedPlaceholder),
            ThreatLevel.Medium => (ModelVisibleContentAction.Redact, RedactedPlaceholder),
            ThreatLevel.High => (ModelVisibleContentAction.Quarantine, AgentGuardrailConventions.DefaultQuarantinePlaceholder),
            ThreatLevel.Critical => (ModelVisibleContentAction.BlockModelIngestion, AgentGuardrailConventions.DefaultBlockedPlaceholder),
            // Defensive against unknown ThreatLevel values from future AGT versions.
            _ => (ModelVisibleContentAction.Allow, originalText),
        };

    private static IReadOnlyList<string> BuildCategories(DetectionResult result)
    {
        if (!result.IsInjection || result.InjectionType == InjectionType.None)
            return [];
        return [result.InjectionType.ToString()];
    }

    private static string BuildReason(DetectionResult result) =>
        result.IsInjection && result.InjectionType != InjectionType.None
            ? result.InjectionType.ToString()
            : AgentGuardrailConventions.Reasons.None;
}
