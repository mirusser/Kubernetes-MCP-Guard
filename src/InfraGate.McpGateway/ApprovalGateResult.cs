namespace InfraGate.McpGateway;

public sealed record ApprovalGateResult(bool IsApproved, string Message)
{
    public static ApprovalGateResult Approved() => new(true, string.Empty);

    public static ApprovalGateResult RequiresApproval(string message) => new(false, message);
}
