namespace InfraGate.Approvals;

public sealed record DomainPolicyCheck(string Code, string Message, string Severity, string? ObjectRef);
