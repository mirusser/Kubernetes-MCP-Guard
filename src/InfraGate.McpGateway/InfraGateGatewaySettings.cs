namespace InfraGate.McpGateway;

internal sealed record InfraGateGatewaySettings
{
    public string? AspNetCoreUrls { get; init; }
    public string? DownstreamAssembly { get; init; }
    public string? DownstreamProject { get; init; }
    public string? GuardAuditRoot { get; init; }
}
