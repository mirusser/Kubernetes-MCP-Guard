namespace InfraGate.McpServer.Models;

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
