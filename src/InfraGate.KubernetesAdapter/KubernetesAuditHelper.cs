using InfraGate.Approvals;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.AuditPayloads;

namespace InfraGate.KubernetesAdapter;

internal static class KubernetesAuditHelper
{
    internal static ApprovalAuditEntry DryRunFailed(
        string phase,
        string planId,
        string operation,
        string namespaceName,
        string[] objects,
        string message)
    {
        var payload = new DryRunFailedPayload(
            phase,
            planId,
            operation,
            namespaceName,
            objects,
            message);

        return string.Equals(phase, KubernetesAdapterConventions.AuditPhases.Request, StringComparison.Ordinal)
            ? new ApprovalAuditEntry(ApprovalConventions.AuditEvents.DryRunFailed, payload)
            : new ApprovalAuditEntry(ApprovalConventions.AuditEvents.DryRunFailed, payload, PlanId: planId);
    }

    internal static ApprovalAuditEntry DiffFailed(
        string? planId,
        string operation,
        string namespaceName,
        string[] objects,
        string message)
    {
        var auditPlanId = planId ?? ApprovalIds.NewPlanId();
        var payload = new DiffFailedPayload(
            auditPlanId,
            operation,
            namespaceName,
            objects,
            message);

        return planId is null
            ? new ApprovalAuditEntry(ApprovalConventions.AuditEvents.DiffFailed, payload)
            : new ApprovalAuditEntry(ApprovalConventions.AuditEvents.DiffFailed, payload, PlanId: auditPlanId);
    }

    internal static ApprovalAuditEntry ApplyDriftDetected(
        string planId,
        string operation,
        string namespaceName,
        string message) =>
        new(
            ApprovalConventions.AuditEvents.ApplyDriftDetected,
            new ApplyDriftDetectedPayload(
                planId,
                operation,
                namespaceName,
                message),
            PlanId: planId);

    internal static ApprovalAuditEntry ApplyDenied(string planId, string message) =>
        new(
            ApprovalConventions.AuditEvents.ApplyDenied,
            new ApplyDeniedPayload(planId, message),
            PlanId: planId);
}
