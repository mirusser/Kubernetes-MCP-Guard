using InfraGate.Approvals.Audit;
namespace InfraGate.Approvals.Plan;

public sealed record class PlanBuildResult(
    bool Succeeded,
    PlanEnvelope? Envelope,
    string PlanId,
    string TargetNamespace,
    string Message,
    string? ReasonCode = null)
{
    public ApprovalAuditEntry? Audit { get; init; }

    public static PlanBuildResult Success(PlanEnvelope envelope, string planId, string targetNamespace) =>
        new(true, envelope, planId, targetNamespace, string.Empty);

    public static PlanBuildResult Failed(string message, string? reasonCode = null) =>
        new(false, null, string.Empty, string.Empty, message, reasonCode);

    public static PlanBuildResult Failed(string message, ApprovalAuditEntry audit, string? reasonCode = null) =>
        new(false, null, string.Empty, string.Empty, message, reasonCode) { Audit = audit };
}
