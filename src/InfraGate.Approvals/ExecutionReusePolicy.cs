namespace InfraGate.Approvals;

public sealed record class ExecutionReusePolicy(string Type)
{
    public ExecutionReusePolicy()
        : this(string.Empty)
    {
    }

    public static ExecutionReusePolicy SingleExecution() =>
        new(ApprovalConventions.ExecutionReusePolicyTypes.SingleExecution);
}
