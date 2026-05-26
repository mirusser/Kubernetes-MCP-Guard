namespace InfraGate.McpGateway.Email;

internal interface ISmtpClientFactory
{
    ISmtpClient Create(SmtpApprovalEmailOptions options);
}
