namespace InfraGate.Approvals;

public interface IDomainPolicyCheck
{
    string Code { get; }

    string Message { get; }

    string Severity { get; }

    string? ObjectRef { get; }
}
