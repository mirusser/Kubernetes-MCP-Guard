using System.Net.Mail;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace InfraGate.McpGateway.Email;

internal sealed class SmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClient Create(SmtpApprovalEmailOptions options)
    {
        options.Validate();
        return new MailKitSmtpAdapter(options);
    }

    private sealed class MailKitSmtpAdapter(SmtpApprovalEmailOptions options) : ISmtpClient
    {
        public bool EnableSsl => options.EnableSsl;

        public async Task SendMailAsync(MailMessage message, CancellationToken cancellationToken)
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(string.Empty, message.From?.Address ?? options.FromAddress));
            foreach (MailAddress addr in message.To)
                mime.To.Add(new MailboxAddress(string.Empty, addr.Address));
            mime.Subject = message.Subject ?? string.Empty;
            mime.Body = new TextPart(TextFormat.Plain) { Text = message.Body ?? string.Empty };

            using var client = new SmtpClient();
            SecureSocketOptions secureOptions = options.EnableSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;
            await client.ConnectAsync(options.Host, options.Port, secureOptions, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(options.Username))
            {
                await client.AuthenticateAsync(options.Username, options.Password ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);
            }
            await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() { }
    }
}
