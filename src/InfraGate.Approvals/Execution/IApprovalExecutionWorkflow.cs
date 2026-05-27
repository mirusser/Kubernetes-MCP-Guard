using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.PreExecution;
namespace InfraGate.Approvals.Execution;

public interface IApprovalExecutionWorkflow
{
    Task<BeginExecutionAttemptResult> BeginExecutionAttemptAsync(
        string planId,
        ApprovalGrant grant,
        CancellationToken cancellationToken);

    Task RecordExecutionBlockedAsync(
        ExecutionAttempt attempt,
        string message,
        string? reasonCode,
        PlanAudit audit,
        CancellationToken cancellationToken);

    Task RecordExecutionFailedAsync(
        ExecutionAttempt attempt,
        string message,
        string? reasonCode,
        PlanAudit audit,
        CancellationToken cancellationToken);

    Task RecordExecutionSucceededAsync(
        ExecutionAttempt attempt,
        ApprovalGrant grant,
        string targetNamespace,
        string message,
        PlanAudit audit,
        CancellationToken cancellationToken);
}
