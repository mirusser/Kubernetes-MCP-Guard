using System.Net.Mail;

namespace InfraGate.McpGateway.Email;

internal interface ISmtpClient : IDisposable
{
    bool EnableSsl { get; }

    Task SendMailAsync(MailMessage message, CancellationToken cancellationToken);
}
