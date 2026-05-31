namespace InfraGate.McpServer.Models;

public sealed record class KubernetesPlanPolicyFinding(
    string Severity,
    string Code,
    string ObjectRef,
    string Message);
