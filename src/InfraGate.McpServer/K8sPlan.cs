namespace InfraGate.McpServer;

public sealed record K8sPlan(
    string Id,
    string Operation,
    string Namespace,
    DateTimeOffset CreatedAtUtc,
    string Description,
    Dictionary<string, string> Parameters,
    K8sObjectRef[] Objects,
    string? Manifest);

public sealed record K8sObjectRef(
    string ApiVersion,
    string Kind,
    string Namespace,
    string Name);
