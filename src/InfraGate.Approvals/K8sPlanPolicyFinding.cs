namespace InfraGate.Approvals;

public sealed record K8sPlanPolicyFinding(
    string Severity,
    string Code,
    string ObjectRef,
    string Message);
