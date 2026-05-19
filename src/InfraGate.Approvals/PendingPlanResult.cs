namespace InfraGate.Approvals;

public sealed record PendingPlanResult(
    bool IsPending,
    PlanEnvelope? Envelope,
    string? Hash,
    string PendingPath,
    string Message)
{
    public static PendingPlanResult Found(PlanEnvelope envelope, string pendingPath, string hash) =>
        new(true, envelope, hash, pendingPath, "Pending.");

    public static PendingPlanResult Denied(string message) =>
        new(false, null, null, string.Empty, message);
}
