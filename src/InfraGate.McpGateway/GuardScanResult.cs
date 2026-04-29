namespace InfraGate.McpGateway;

public sealed record GuardScanResult(IReadOnlyList<GuardrailFinding> Findings)
{
    public bool HasFindings => Findings.Count > 0;

    public string[] Categories =>
        Findings.Select(finding => finding.Category).Distinct(StringComparer.Ordinal).OrderBy(category => category).ToArray();
}
