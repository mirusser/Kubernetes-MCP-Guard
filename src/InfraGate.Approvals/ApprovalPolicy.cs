namespace InfraGate.Approvals;

public sealed record ApprovalPolicy(string Type)
{
    public ApprovalPolicy()
        : this(string.Empty)
    {
    }

    public static ApprovalPolicy SameSubject() =>
        new(ApprovalConventions.ApprovalPolicyTypes.SameSubject);
}
