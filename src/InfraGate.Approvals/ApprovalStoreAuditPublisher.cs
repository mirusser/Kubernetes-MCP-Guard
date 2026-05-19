namespace InfraGate.Approvals;

public sealed class ApprovalStoreAuditPublisher(ApprovalStore approvalStore) : IApprovalAuditPublisher
{
    public Task PublishAsync(PlanAudit audit, CancellationToken cancellationToken) =>
        approvalStore.WriteAuditAsync(audit.EventName, audit.Payload, cancellationToken);
}
