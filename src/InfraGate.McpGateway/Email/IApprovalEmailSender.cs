namespace InfraGate.McpGateway.Email;

public interface IApprovalEmailSender
{
    Task SendAsync(ApprovalEmailContent content, CancellationToken cancellationToken);
}
