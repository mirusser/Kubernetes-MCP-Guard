namespace InfraGate.Approvals;

public sealed class NoOpApprovalAuditPublisher : IApprovalAuditPublisher
{
    public static NoOpApprovalAuditPublisher Instance { get; } = new();

    private NoOpApprovalAuditPublisher()
    {
    }

    public Task PublishAsync(PlanAudit audit, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
