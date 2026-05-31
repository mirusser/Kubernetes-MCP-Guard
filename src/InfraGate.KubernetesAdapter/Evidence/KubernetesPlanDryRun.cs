namespace InfraGate.KubernetesAdapter.Evidence;

public sealed record class KubernetesPlanDryRun(
    string Status,
    DateTimeOffset CheckedAtUtc,
    KubernetesPlanDryRunObject[] Objects,
    string[] Warnings,
    string Message);
