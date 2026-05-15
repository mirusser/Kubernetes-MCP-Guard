namespace InfraGate.KubernetesAdapter;

public sealed record KubernetesPlanPayload
{
    public KubernetesPlanPayload() { }

    public KubernetesPlanPayload(
        string namespaceName,
        string description,
        Dictionary<string, string> parameters,
        K8sObjectRef[] objects)
    {
        Namespace = namespaceName;
        Description = description;
        Parameters = parameters;
        Objects = objects;
    }

    public string Namespace { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public Dictionary<string, string> Parameters { get; init; } = [];

    public K8sObjectRef[] Objects { get; init; } = [];

    public string? Manifest { get; init; }

    public K8sPlanDryRun? DryRun { get; init; }

    public K8sPlanDiff[] Diffs { get; init; } = [];

    public K8sPlanPolicyFinding[] PolicyFindings { get; init; } = [];
}
