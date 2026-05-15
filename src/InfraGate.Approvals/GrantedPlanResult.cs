namespace InfraGate.Approvals;

public sealed record GrantedPlanResult(
    bool IsGranted,
    bool GrantExists,
    PlanEnvelope? Envelope,
    ApprovalGrant? Grant,
    string Message)
{
    public static GrantedPlanResult Granted(PlanEnvelope envelope, ApprovalGrant grant) =>
        new(true, true, envelope, grant, "Granted.");

    public static GrantedPlanResult MissingGrant(string message) =>
        new(false, false, null, null, message);

    public static GrantedPlanResult Denied(string message, bool grantExists = true) =>
        new(false, grantExists, null, null, message);
}
