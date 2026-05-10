namespace InfraGate.McpServer.Policy;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
internal sealed record K8sPolicyResult(IReadOnlyList<K8sPolicyFinding> Findings)
{
    public bool IsDenied => Findings.Any(f => f.Severity == K8sPolicySeverity.Deny);

    public string FormatRefusal() =>
        string.Join(Environment.NewLine, Findings
            .Where(f => f.Severity == K8sPolicySeverity.Deny)
            .Select(f => $"  [{f.Code}] {f.Message} ({f.ObjectRef})"));

    public string FormatWarnings() =>
        string.Join(Environment.NewLine, Findings
            .Where(f => f.Severity == K8sPolicySeverity.Warning)
            .Select(f => $"  [{f.Code}] {f.Message} ({f.ObjectRef})"));
}
