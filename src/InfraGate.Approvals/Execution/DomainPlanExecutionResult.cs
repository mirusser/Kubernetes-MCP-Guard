using InfraGate.Approvals.Audit;
namespace InfraGate.Approvals.Execution;

public sealed record class DomainPlanExecutionResult(
    bool IsSuccessful,
    string Message,
    string? TargetNamespace,
    string? ReasonCode = null)
{
    public ApprovalAuditEntry? Audit { get; init; }

    public static DomainPlanExecutionResult Success(string message, string? targetNamespace) =>
        new(true, message, targetNamespace);

    public static DomainPlanExecutionResult Blocked(string message, string? reasonCode = null) =>
        new(false, message, TargetNamespace: null, reasonCode);

    public static DomainPlanExecutionResult Blocked(string message, ApprovalAuditEntry audit, string? reasonCode = null) =>
        new(false, message, TargetNamespace: null, reasonCode) { Audit = audit };
}
