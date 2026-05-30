namespace InfraGate.McpGateway;

public sealed record class GuardScanResult(IReadOnlyList<GuardrailFinding> Findings)
{
    public bool HasFindings => Findings.Count > 0;

    private string[]? categories;
    public string[] Categories => categories ??= Findings.Select(f => f.Category).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToArray();
}
