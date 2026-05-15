namespace InfraGate.Approvals;

public sealed record ApprovedPlanResult(bool IsApproved, PlanEnvelope? Envelope, string? Hash, string Message)
{
    public static ApprovedPlanResult Approved(PlanEnvelope envelope, string hash) =>
        new(true, envelope, hash, "Approved.");

    public static ApprovedPlanResult Denied(string message) =>
        new(false, null, null, message);
}
