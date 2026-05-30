namespace InfraGate.AgentMcp;

public sealed class AgentMcpOptions
{
    public string GatewayBaseUrl { get; init; } = string.Empty;
    public string ClientName { get; init; } = "infra-gate-agent";

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(GatewayBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientName);
    }
}
