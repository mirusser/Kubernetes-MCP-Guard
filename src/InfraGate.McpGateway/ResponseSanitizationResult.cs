namespace InfraGate.McpGateway;

public sealed record ResponseSanitizationResult(
    string Text,
    IReadOnlyList<GuardrailFinding> Findings,
    bool ManifestRedacted)
{
    public bool HasFindings => Findings.Count > 0;

    private string[]? categories;
    public string[] Categories => categories ??= Findings.Select(f => f.Category).Distinct(StringComparer.Ordinal).OrderBy(c => c).ToArray();
}
