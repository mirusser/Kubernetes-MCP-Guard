namespace InfraGate.KubernetesAdapter.Policy;

public sealed record class KubernetesPolicyResult(IReadOnlyList<KubernetesPolicyFinding> Findings)
{
    public bool HadError { get; init; }

    public bool IsDenied => Findings.Any(f => f.Severity == KubernetesPolicySeverity.Deny);

    public string FormatRefusal() =>
        string.Join(Environment.NewLine, Findings
            .Where(f => f.Severity == KubernetesPolicySeverity.Deny)
            .Select(f => $"  [{f.Code}] {f.Message} ({f.ObjectRef})"));

    public string FormatWarnings() =>
        string.Join(Environment.NewLine, Findings
            .Where(f => f.Severity == KubernetesPolicySeverity.Warning)
            .Select(f => $"  [{f.Code}] {f.Message} ({f.ObjectRef})"));
}
