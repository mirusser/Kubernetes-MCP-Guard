using System.Net.Mail;

namespace InfraGate.McpGateway.Email;

internal sealed class SmtpApprovalEmailSender(
    SmtpApprovalEmailOptions options,
    ISmtpClientFactory smtpClientFactory) : IApprovalEmailSender
{
    public async Task SendAsync(ApprovalEmailContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        options.Validate();

        using var message = new MailMessage(
            options.FromAddress,
            content.ToAddress,
            content.Subject,
            content.BodyPlaintext);

        using var client = smtpClientFactory.Create(options);
        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
