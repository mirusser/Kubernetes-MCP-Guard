namespace InfraGate.McpGateway;

internal sealed record InfraGateApprovalSettings
{
    public string? Root { get; init; }
    public string? BaseUrl { get; init; }
    public string? ChallengeTtlSeconds { get; init; }
}
