using InfraGate.KubernetesAdapter.Evidence;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Tests.UnitTests;

internal sealed class FakeEvidenceService : IKubernetesEvidenceService
{
    public KubernetesApplyEvidence? ApplyEvidenceResult { get; set; }

    public KubernetesPlanDryRun? DryRunResult { get; set; } = CreateDryRun();

    public KubernetesPlanDiff[]? DiffsResult { get; set; } = [CreateDiff()];

    public KubernetesApplyEvidence? CheckApplyEvidenceResult { get; set; }

    public Task<KubernetesApplyEvidence?> GetApplyEvidenceAsync(
        string namespaceName,
        string manifest,
        CancellationToken ct) =>
        Task.FromResult<KubernetesApplyEvidence?>(ApplyEvidenceResult ?? new KubernetesApplyEvidence(DryRunResult!, [], false, null));

    public Task<KubernetesPlanDryRun?> GetDryRunAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct) =>
        Task.FromResult(DryRunResult);

    public Task<KubernetesPlanDiff[]?> GetDiffsAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct) =>
        Task.FromResult(DiffsResult);

    public Task<KubernetesApplyEvidence?> CheckApplyDryRunAsync(
        string namespaceName,
        string manifest,
        CancellationToken ct) =>
        Task.FromResult<KubernetesApplyEvidence?>(CheckApplyEvidenceResult ?? ApplyEvidenceResult ?? new KubernetesApplyEvidence(DryRunResult!, [], false, null));

    private static KubernetesPlanDryRun CreateDryRun() =>
        new(
            "succeeded",
            DateTimeOffset.UtcNow,
            [new KubernetesPlanDryRunObject(
                $"{KubernetesAdapterConventions.ApiVersions.AppsV1} {KubernetesAdapterConventions.ResourceKinds.Deployment} demo/nginx",
                "{}")],
            [],
            "Server-side dry-run succeeded.");

    private static KubernetesPlanDiff CreateDiff() =>
        new(
            new KubernetesObjectRef(
                KubernetesAdapterConventions.ApiVersions.AppsV1,
                KubernetesAdapterConventions.ResourceKinds.Deployment,
                "demo",
                "nginx"),
            "update",
            $"Update {KubernetesAdapterConventions.ApiVersions.AppsV1} {KubernetesAdapterConventions.ResourceKinds.Deployment} demo/nginx",
            "@@ -1 +1 @@",
            "{}",
            "{}",
            [],
            [],
            []);
}
