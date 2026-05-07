namespace InfraGate.McpServer.Policy;

internal sealed record K8sPolicyFinding(
    K8sPolicySeverity Severity,
    string Code,
    string ObjectRef,
    string Message);
