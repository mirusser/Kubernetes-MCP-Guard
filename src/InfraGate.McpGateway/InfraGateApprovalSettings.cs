namespace InfraGate.McpGateway;

internal sealed record class InfraGateApprovalSettings
{
    public string? Root { get; init; }
    public string? BaseUrl { get; init; }
    public string? ChallengeTtlSeconds { get; init; }
}
