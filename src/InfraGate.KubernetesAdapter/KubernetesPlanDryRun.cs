namespace InfraGate.KubernetesAdapter;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record class KubernetesPlanDryRun(
    string Status,
    DateTimeOffset CheckedAtUtc,
    KubernetesPlanDryRunObject[] Objects,
    string[] Warnings,
    string Message);
