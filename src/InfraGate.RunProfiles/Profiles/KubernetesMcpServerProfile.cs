namespace InfraGate.RunProfiles;

// Secondary, read-only-only downstream MCP process (see docs/adr for the decision
// record). read_only and enabled_tools are intentionally not YAML-configurable —
// nothing downstream is prepared to route mutations through this client yet.
internal static class KubernetesMcpServerProfile
{
    public const bool ReadOnly = true;

    public static readonly IReadOnlyList<string> EnabledTools =
    [
        "pods_list",
        "pods_get",
        "pods_log",
        "events_list",
        "resources_list",
        "resources_get",
    ];
}
