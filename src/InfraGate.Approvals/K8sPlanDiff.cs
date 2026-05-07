namespace InfraGate.Approvals;

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
