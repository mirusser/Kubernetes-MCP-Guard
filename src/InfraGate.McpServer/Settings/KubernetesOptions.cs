namespace InfraGate.McpServer.Settings;

/// <summary>
/// Strongly-typed McpServer configuration bound from the <c>InfraGate:Kubernetes</c> section
/// (see <see cref="SectionName"/>). The framework binder matches property names to configuration
/// keys recursively — there is no manual env-var mapping or per-key reads.
/// </summary>
public sealed record class KubernetesOptions
{
    public const string SectionName = "InfraGate:Kubernetes";

    public string? KubeConfig { get; init; }
    public bool UseInClusterConfig { get; init; }
    public IReadOnlyList<string> AllowedNamespaces { get; init; } = [];
    public string? LogPath { get; init; }
}
