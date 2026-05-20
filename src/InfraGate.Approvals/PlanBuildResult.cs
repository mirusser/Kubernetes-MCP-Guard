namespace InfraGate.Approvals;

public sealed record PlanBuildResult(
    bool Succeeded,
    PlanEnvelope? Envelope,
    string PlanId,
    string TargetNamespace,
    string Message,
    string? ReasonCode = null)
{
    public PlanAudit? Audit { get; init; }

    public static PlanBuildResult Success(PlanEnvelope envelope, string planId, string targetNamespace) =>
        new(true, envelope, planId, targetNamespace, string.Empty);

    public static PlanBuildResult Failed(string message, string? reasonCode = null) =>
        new(false, null, string.Empty, string.Empty, message, reasonCode);

    public static PlanBuildResult Failed(string message, PlanAudit audit, string? reasonCode = null) =>
        new(false, null, string.Empty, string.Empty, message, reasonCode) { Audit = audit };
}
