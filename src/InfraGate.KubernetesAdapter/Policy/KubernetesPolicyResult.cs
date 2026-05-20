namespace InfraGate.KubernetesAdapter.Policy;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record class KubernetesPolicyResult(IReadOnlyList<KubernetesPolicyFinding> Findings)
{
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
