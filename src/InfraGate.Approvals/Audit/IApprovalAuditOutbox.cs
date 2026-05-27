namespace InfraGate.Approvals.Audit;

public interface IApprovalAuditOutbox
{
    Task<long> AppendAsync(ApprovalAuditEntry entry, CancellationToken cancellationToken);
}
