namespace InfraGate.KubernetesAdapter.Evidence;

internal interface IKubernetesEvidenceService
{
    Task<KubernetesApplyEvidence?> GetApplyEvidenceAsync(string namespaceName, string manifest, CancellationToken ct);

    Task<KubernetesPlanDryRun?> GetDryRunAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct);

    Task<KubernetesPlanDiff[]?> GetDiffsAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct);

    Task<KubernetesApplyEvidence?> CheckApplyDryRunAsync(string namespaceName, string manifest, CancellationToken ct);
}
