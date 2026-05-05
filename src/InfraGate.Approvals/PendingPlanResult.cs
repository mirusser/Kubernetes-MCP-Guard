namespace InfraGate.Approvals;

public sealed record PendingPlanResult(
    bool IsPending,
    K8sPlan? Plan,
    string? Hash,
    string PendingPath,
    string ApprovedPath,
    string Message)
{
    public static PendingPlanResult Found(K8sPlan plan, string pendingPath, string approvedPath, string hash) =>
        new(true, plan, hash, pendingPath, approvedPath, "Pending.");

    public static PendingPlanResult Denied(string message) =>
        new(false, null, null, string.Empty, string.Empty, message);
}
