namespace InfraGate.Approvals;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record K8sPlan
{
    public K8sPlan() { }

    public K8sPlan(
        string id,
        string operation,
        string namespaceName,
        DateTimeOffset createdAtUtc,
        string description,
        Dictionary<string, string> parameters,
        K8sObjectRef[] objects)
    {
        Id = id;
        Operation = operation;
        Namespace = namespaceName;
        CreatedAtUtc = createdAtUtc;
        Description = description;
        Parameters = parameters;
        Objects = objects;
    }

    public string Id { get; init; } = string.Empty;

    public string Operation { get; init; } = string.Empty;

    public string Namespace { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public string Description { get; init; } = string.Empty;

    public Dictionary<string, string> Parameters { get; init; } = [];

    public K8sObjectRef[] Objects { get; init; } = [];

    public string? Manifest { get; init; }

    public K8sPlanDryRun? DryRun { get; init; }

    public K8sPlanDiff[] Diffs { get; init; } = [];

    public K8sPlanPolicyFinding[] PolicyFindings { get; init; } = [];
}
