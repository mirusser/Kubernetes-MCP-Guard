namespace InfraGate.Approvals;

public interface IApprovalAuditPublisher
{
    Task PublishAsync(PlanAudit audit, CancellationToken cancellationToken);
}
