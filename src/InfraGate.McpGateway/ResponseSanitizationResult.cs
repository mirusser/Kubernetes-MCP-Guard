namespace InfraGate.McpGateway;

public sealed record ResponseSanitizationResult(
    string Text,
    IReadOnlyList<GuardrailFinding> Findings,
    bool ManifestRedacted)
{
    public bool HasFindings => Findings.Count > 0;

    public string[] Categories =>
        Findings.Select(finding => finding.Category).Distinct(StringComparer.Ordinal).OrderBy(category => category).ToArray();
}
