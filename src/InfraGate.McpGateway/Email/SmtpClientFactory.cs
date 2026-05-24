using System.Net;
using System.Net.Mail;

namespace InfraGate.McpGateway.Email;

internal sealed class SmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClient Create(SmtpApprovalEmailOptions options)
    {
        options.Validate();

        var client = new SmtpClient(options.Host, options.Port);
        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            client.Credentials = new NetworkCredential(options.Username, options.Password);
        }

        return new SmtpClientAdapter(client);
    }

    private sealed class SmtpClientAdapter(SmtpClient client) : ISmtpClient
    {
        public Task SendMailAsync(MailMessage message, CancellationToken cancellationToken) =>
            client.SendMailAsync(message, cancellationToken);

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
