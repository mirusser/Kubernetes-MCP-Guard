using InfraGate.Approvals.AuditPayloads;

namespace InfraGate.Approvals;

public sealed record PreExecutionGateResult(
    bool IsPassed,
    string Message,
    PlanEnvelope? Envelope,
    ApprovalGrant? Grant)
{
    public PlanAudit? Audit { get; init; }

    public static PreExecutionGateResult Passed(PlanEnvelope envelope, ApprovalGrant grant) =>
        new(true, "Pre-execution gates passed.", envelope, grant);

    public static PreExecutionGateResult Blocked(string planId, string message) =>
        new(false, message, Envelope: null, Grant: null)
        {
            Audit = new PlanAudit(
                ApprovalConventions.AuditEvents.ApplyDenied,
                new ApplyDeniedPayload(planId, message))
        };

    public static PreExecutionGateResult Blocked(DomainPlanExecutionResult domainResult, string planId) =>
        new(false, domainResult.Message, Envelope: null, Grant: null)
        {
            Audit = domainResult.Audit ?? new PlanAudit(
                ApprovalConventions.AuditEvents.ApplyDenied,
                new ApplyDeniedPayload(planId, domainResult.Message))
        };
}
