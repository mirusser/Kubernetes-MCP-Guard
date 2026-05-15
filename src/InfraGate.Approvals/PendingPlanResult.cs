namespace InfraGate.Approvals;

public sealed record PendingPlanResult(
    bool IsPending,
    PlanEnvelope? Envelope,
    string? Hash,
    string PendingPath,
    string ApprovedPath,
    string Message)
{
    public static PendingPlanResult Found(PlanEnvelope envelope, string pendingPath, string approvedPath, string hash) =>
        new(true, envelope, hash, pendingPath, approvedPath, "Pending.");

    public static PendingPlanResult Denied(string message) =>
        new(false, null, null, string.Empty, string.Empty, message);
}
