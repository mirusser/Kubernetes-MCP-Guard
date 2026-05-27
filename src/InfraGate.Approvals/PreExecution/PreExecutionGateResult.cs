using InfraGate.Approvals.AuditPayloads;

using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.Execution;
namespace InfraGate.Approvals.PreExecution;

public sealed record class PreExecutionGateResult(
    bool IsPassed,
    string Message,
    PlanEnvelope? Envelope,
    ApprovalGrant? Grant,
    string? ReasonCode = null)
{
    public PlanAudit? Audit { get; init; }

    public static PreExecutionGateResult Passed(PlanEnvelope envelope, ApprovalGrant grant) =>
        new(true, "Pre-execution gates passed.", envelope, grant);

    public static PreExecutionGateResult Blocked(string planId, string message, string? reasonCode = null) =>
        new(false, message, Envelope: null, Grant: null, reasonCode)
        {
            Audit = new PlanAudit(
                ApprovalConventions.AuditEvents.ApplyDenied,
                new ApplyDeniedPayload(planId, message))
        };

    public static PreExecutionGateResult Blocked(DomainPlanExecutionResult domainResult, string planId) =>
        new(false, domainResult.Message, Envelope: null, Grant: null, domainResult.ReasonCode)
        {
            Audit = domainResult.Audit ?? new PlanAudit(
                ApprovalConventions.AuditEvents.ApplyDenied,
                new ApplyDeniedPayload(planId, domainResult.Message))
        };
}
