namespace InfraGate.McpGateway.Email;

public sealed record class SmtpApprovalEmailOptions(
    string Host,
    int Port,
    string FromAddress,
    string? Username = null,
    string? Password = null,
    bool EnableSsl = SmtpApprovalEmailOptions.DefaultEnableSsl)
{
    public const int DefaultPort = 25;
    public const bool DefaultEnableSsl = true;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(FromAddress);
        if (Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("SMTP port must be between 1 and 65535.");
        }
    }
}
