namespace InfraGate.McpGateway;

internal sealed record class InfraGateApprovalSettings
{
    public string? Root { get; init; }
    public string? BaseUrl { get; init; }
    public string? ChallengeTtlSeconds { get; init; }
    public string? OperatorGroup { get; init; }
    public string? OperatorEmail { get; init; }
    public InfraGateApprovalSmtpSettings? Smtp { get; init; }
}

internal sealed record class InfraGateApprovalSmtpSettings
{
    public string? Host { get; init; }
    public string? Port { get; init; }
    public string? From { get; init; }
    public string? User { get; init; }
    public string? Password { get; init; }
}
