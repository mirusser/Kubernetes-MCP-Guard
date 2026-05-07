using System.Text.Json.Serialization;

namespace InfraGate.Approvals;

public sealed record K8sPlan
{
    [JsonConstructor]
    public K8sPlan(
        string Id,
        string Operation,
        string Namespace,
        DateTimeOffset CreatedAtUtc,
        string Description,
        Dictionary<string, string> Parameters,
        K8sObjectRef[] Objects,
        string? Manifest,
        K8sPlanDryRun? DryRun = null,
        K8sPlanDiff[]? Diffs = null,
        K8sPlanPolicyFinding[]? PolicyFindings = null)
    {
        this.Id = Id;
        this.Operation = Operation;
        this.Namespace = Namespace;
        this.CreatedAtUtc = CreatedAtUtc;
        this.Description = Description;
        this.Parameters = Parameters;
        this.Objects = Objects;
        this.Manifest = Manifest;
        this.DryRun = DryRun;
        this.Diffs = Diffs ?? [];
        this.PolicyFindings = PolicyFindings ?? [];
    }

    public string Id { get; init; }

    public string Operation { get; init; }

    public string Namespace { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public string Description { get; init; }

    public Dictionary<string, string> Parameters { get; init; }

    public K8sObjectRef[] Objects { get; init; }

    public string? Manifest { get; init; }

    public K8sPlanDryRun? DryRun { get; init; }

    public K8sPlanDiff[] Diffs { get; init; }

    public K8sPlanPolicyFinding[] PolicyFindings { get; init; }
}
