using InfraGate.KubernetesAdapter.Evidence;

namespace InfraGate.KubernetesAdapter.PlanBuilding;

public sealed record class KubernetesPlanPayload
{
    public KubernetesPlanPayload() { }

    public KubernetesPlanPayload(
        string namespaceName,
        string description,
        Dictionary<string, string> parameters,
        KubernetesObjectRef[] objects)
    {
        Namespace = namespaceName;
        Description = description;
        Parameters = parameters;
        Objects = objects;
    }

    public string Namespace { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public Dictionary<string, string> Parameters { get; init; } = [];

    public KubernetesObjectRef[] Objects { get; init; } = [];

    public string? Manifest { get; init; }

    public KubernetesPlanDryRun? DryRun { get; init; }

    public KubernetesPlanDiff[] Diffs { get; init; } = [];

    public KubernetesPlanPolicyFinding[] PolicyFindings { get; init; } = [];
}
