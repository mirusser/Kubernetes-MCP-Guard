namespace InfraGate.McpServer.Models;

public sealed record class KubernetesObjectRef(
    string ApiVersion,
    string Kind,
    string Namespace,
    string Name);
