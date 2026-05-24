using System.Net.Mail;

namespace InfraGate.McpGateway.Email;

internal interface ISmtpClient : IDisposable
{
    Task SendMailAsync(MailMessage message, CancellationToken cancellationToken);
}
