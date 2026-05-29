namespace InfraGate.McpServer.Models;

public sealed record class KubernetesPlanDryRunObject(
    string Object,
    string ResponseJson);
