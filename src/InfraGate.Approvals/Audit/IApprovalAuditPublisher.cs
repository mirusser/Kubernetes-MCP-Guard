namespace InfraGate.Approvals.Audit;

public interface IApprovalAuditPublisher
{
    Task PublishAsync(PlanAudit audit, CancellationToken cancellationToken);
}
