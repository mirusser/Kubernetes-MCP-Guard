using System.Net.Mail;

namespace InfraGate.McpGateway.Email;

internal sealed class SmtpApprovalEmailSender(
    SmtpApprovalEmailOptions options,
    ISmtpClientFactory smtpClientFactory,
    ILogger<SmtpApprovalEmailSender> logger) : IApprovalEmailSender
{
    public async Task SendAsync(ApprovalEmailContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        options.Validate();

        logger.LogInformation(
            "smtp email queued host={Host}:{Port} ssl={Ssl}",
            options.Host, options.Port, options.EnableSsl);

        using var message = new MailMessage(
            options.FromAddress,
            content.ToAddress,
            content.Subject,
            content.BodyPlaintext);

        using var client = smtpClientFactory.Create(options);
        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("smtp email sent");
    }
}
