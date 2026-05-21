namespace InfraGate.Approvals;

public sealed record class GrantedPlanResult(
    bool IsGranted,
    bool GrantExists,
    PlanEnvelope? Envelope,
    ApprovalGrant? Grant,
    string Message,
    string? ReasonCode = null)
{
    public static GrantedPlanResult Granted(PlanEnvelope envelope, ApprovalGrant grant) =>
        new(true, true, envelope, grant, "Granted.");

    public static GrantedPlanResult MissingGrant(string message, string? reasonCode = null) =>
        new(false, false, null, null, message, reasonCode);

    public static GrantedPlanResult Denied(
        string message,
        bool grantExists = true,
        string? reasonCode = null) =>
        new(false, grantExists, null, null, message, reasonCode);
}
