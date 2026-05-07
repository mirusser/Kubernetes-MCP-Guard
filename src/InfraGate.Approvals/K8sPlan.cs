namespace InfraGate.Approvals;

public sealed record K8sPlan(
    string Id,
    string Operation,
    string Namespace,
    DateTimeOffset CreatedAtUtc,
    string Description,
    Dictionary<string, string> Parameters,
    K8sObjectRef[] Objects,
    string? Manifest,
    K8sPlanDryRun? DryRun = null);
