namespace InfraGate.Approvals;

public sealed record class PendingPlanResult(
    bool IsPending,
    PlanEnvelope? Envelope,
    string? Hash,
    string PendingPath,
    string Message,
    string? ReasonCode = null)
{
    public static PendingPlanResult Found(PlanEnvelope envelope, string pendingPath, string hash) =>
        new(true, envelope, hash, pendingPath, "Pending.");

    public static PendingPlanResult Denied(string message, string? reasonCode = null) =>
        new(false, null, null, string.Empty, message, reasonCode);
}
