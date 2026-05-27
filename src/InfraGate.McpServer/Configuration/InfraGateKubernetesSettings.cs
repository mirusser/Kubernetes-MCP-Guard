namespace InfraGate.McpServer;

internal sealed record class InfraGateKubernetesSettings
{
    public string? KubeConfig { get; init; }
    public bool? UseInClusterConfig { get; init; }
    public IReadOnlyList<string>? AllowedNamespaces { get; init; }
    public string? LogPath { get; init; }
}
