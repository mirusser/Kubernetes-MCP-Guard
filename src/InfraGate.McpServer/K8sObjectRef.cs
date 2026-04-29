namespace InfraGate.McpServer;

public sealed record K8sObjectRef(
    string ApiVersion,
    string Kind,
    string Namespace,
    string Name);
