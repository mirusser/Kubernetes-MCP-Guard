namespace InfraGate.RunProfiles;

// Secondary, read-only-only downstream MCP process (see docs/adr for the decision
// record). Policy fields are intentionally not YAML-configurable — nothing
// downstream is prepared to route mutations through this client.
internal sealed record class KubernetesMcpServerProfile(
    string Kubeconfig,
    string Context,
    IReadOnlyList<string> AllowedNamespaces)
{
    public const bool IsReadOnly = true;
    public const bool AreDestructiveToolsDisabled = true;
    public const bool IsStateless = true;
    public const string ClusterAuthMode = "kubeconfig";
    public const string ClusterProviderStrategy = "disabled";

    public static readonly IReadOnlyList<string> Toolsets = ["core"];

    public static readonly IReadOnlyList<string> EnabledTools =
    [
        "pods_list_in_namespace",
        "pods_get",
        "pods_log",
    ];
}
