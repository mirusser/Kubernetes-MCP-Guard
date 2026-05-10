namespace InfraGate.Approvals;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record K8sPlanDiff(
    K8sObjectRef Object,
    string ChangeType,
    string Summary,
    string UnifiedDiff,
    string? LiveObjectJson,
    string? ProposedObjectJson,
    string[] AddedPaths,
    string[] RemovedPaths,
    string[] ChangedPaths);
