namespace InfraGate.Approvals;

public sealed record PlanBuildResult(
    bool Succeeded,
    PlanEnvelope? Envelope,
    string PlanId,
    string TargetNamespace,
    string Message)
{
    public PlanAudit? Audit { get; init; }

    public static PlanBuildResult Success(PlanEnvelope envelope, string planId, string targetNamespace) =>
        new(true, envelope, planId, targetNamespace, string.Empty);

    public static PlanBuildResult Failed(string message) =>
        new(false, null, string.Empty, string.Empty, message);

    public static PlanBuildResult Failed(string message, PlanAudit audit) =>
        new(false, null, string.Empty, string.Empty, message) { Audit = audit };
}
