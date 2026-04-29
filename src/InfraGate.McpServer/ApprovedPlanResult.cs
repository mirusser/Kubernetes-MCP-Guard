namespace InfraGate.McpServer;

public sealed record ApprovedPlanResult(bool IsApproved, K8sPlan? Plan, string? Hash, string Message)
{
    public static ApprovedPlanResult Approved(K8sPlan plan, string hash) =>
        new(true, plan, hash, "Approved.");

    public static ApprovedPlanResult Denied(string message) =>
        new(false, null, null, message);
}
