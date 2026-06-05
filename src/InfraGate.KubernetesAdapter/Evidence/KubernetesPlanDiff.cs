using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Evidence;

public sealed record class KubernetesPlanDiff(
    KubernetesObjectRef Object,
    string ChangeType,
    string Summary,
    string UnifiedDiff,
    string? LiveObjectJson,
    string? ProposedObjectJson,
    string[] AddedPaths,
    string[] RemovedPaths,
    string[] ChangedPaths,
    string? ResourceVersion = null);
