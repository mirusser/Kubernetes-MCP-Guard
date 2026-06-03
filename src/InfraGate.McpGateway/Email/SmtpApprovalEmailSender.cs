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
            "smtp sending email host={Host}:{Port} ssl={Ssl} from={From} to={To} subject={Subject}",
            options.Host, options.Port, options.EnableSsl,
            options.FromAddress, content.ToAddress, content.Subject);

        using var message = new MailMessage(
            options.FromAddress,
            content.ToAddress,
            content.Subject,
            content.BodyPlaintext);

        try
        {
            using var client = smtpClientFactory.Create(options);
            await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("smtp email sent to {To}", content.ToAddress);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "smtp email failed host={Host}:{Port} to={To}",
                options.Host, options.Port, content.ToAddress);
            throw;
        }
    }
}
