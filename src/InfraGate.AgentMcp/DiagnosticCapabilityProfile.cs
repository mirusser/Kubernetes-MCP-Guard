using System.Text.Json;

namespace InfraGate.AgentMcp;

// Stable reason an MCP tool was excluded from agent discovery. Logged, not thrown: exclusion is
// expected steady-state behavior (every dry-run/diff/check tool and every destructive tool is
// excluded on every call), not an error.
internal enum DiagnosticCapabilityExclusionReason
{
    // Tool is not marked ReadOnlyHint=true. Covers every InfraGate.McpServer.Tools.KubernetesTools
    // Destructive=true tool (apply_manifest, delete_manifest, scale_deployment, restart_deployment,
    // set_deployment_image).
    NotReadOnly,

    // Tool name is not one of the reviewed diagnostic reads below. Covers genuinely unknown tools
    // and the nine dry-run/diff/check evidence tools, which the MCP SDK marks ReadOnlyHint=true
    // (they don't mutate live state) even though they are mutation-preview tools, not diagnostic
    // reads, and are deliberately left out of this shared profile — per-agent mutation/proposal
    // capabilities are added explicitly by each agent's own composition, not through this profile.
    NotProfiled,

    // Tool name is profiled, but its declared JSON Schema properties no longer match the pinned
    // set — e.g. the downstream added, removed, or renamed a parameter since this profile was
    // written. Fails closed rather than trusting an unreviewed schema.
    SchemaDrifted,
}

// Curated, name-and-schema-pinned projection of MCP tools that are safe for agents (Observer,
// Planner) to discover and call as read evidence. ReadOnlyHint alone is not sufficient authority:
// InfraGate.McpServer marks its dry-run/diff/check evidence tools ReadOnlyHint=true too (they don't
// mutate live state), and a downstream tool can add or rename a parameter without changing its name
// or its ReadOnlyHint. This profile pins both the tool name and its expected input property names,
// so AgentMcpToolset only authorizes tools it has actually reviewed.
//
// The tool names and property sets below are independently duplicated from
// InfraGate.McpServer.KubernetesConventions.ToolNames (primary) and
// InfraGate.McpGateway.McpGatewayConventions.SecondaryDownstream.ApprovedTools (secondary) — both
// internal to projects this client-side agent library should not reference. This mirrors the
// existing precedent between KubernetesMcpServerProfile.EnabledTools and
// McpGatewayConventions.SecondaryDownstream.ApprovedTools, which are themselves two independent,
// non-cross-referenced copies of the same three secondary tool names.
public static class DiagnosticCapabilityProfile
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ProfiledTools =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            // Primary: InfraGate.McpServer reviewed diagnostic reads (Tools/KubernetesTools.cs).
            // Deliberately excludes get_allowed_namespaces' sibling dry-run/diff/check tools and
            // every Destructive=true tool.
            ["get_allowed_namespaces"] = Props(),
            ["get_k8s_status"] = Props("namespace", "labelSelector"),
            ["get_k8s_events"] = Props("namespace", "labelSelector", "fieldSelector", "limit", "excludeEventTypes"),
            ["get_pod_logs"] = Props("namespace", "podName", "container", "tailLines", "previous"),
            ["get_k8s_resource"] = Props("namespace", "kind", "name"),
            ["get_deployment_diagnostics"] = Props("namespace", "name", "limit"),
            ["get_pod_diagnostics"] = Props("namespace", "podName", "limit"),
            ["get_service_diagnostics"] = Props("namespace", "name", "limit"),

            // Secondary: approved kubernetes-mcp-server tools (McpGatewayConventions.
            // SecondaryDownstream.ApprovedTools), property sets captured from the pinned binary's
            // real tools/list response.
            ["pods_list_in_namespace"] = Props("namespace", "fieldSelector", "labelSelector"),
            ["pods_get"] = Props("namespace", "name"),
            ["pods_log"] = Props("namespace", "name", "container", "tail", "previous"),
        };

    // The names of every profiled diagnostic read. This is the single shared source of truth for
    // agent-side guardrail allow-lists (Observer, Planner): a tool is only callable by an agent if
    // it is both offered by AgentMcpToolset.GetAgentToolsAsync (via IsAuthorized below) and present
    // in this set, so the two can never drift apart.
    public static IReadOnlySet<string> ToolNames { get; } =
        new HashSet<string>(ProfiledTools.Keys, StringComparer.Ordinal);

    internal static bool IsAuthorized(McpClientTool tool, out DiagnosticCapabilityExclusionReason reason)
    {
        ArgumentNullException.ThrowIfNull(tool);

        Tool protocolTool = tool.ProtocolTool;

        if (protocolTool.Annotations?.ReadOnlyHint != true)
        {
            reason = DiagnosticCapabilityExclusionReason.NotReadOnly;
            return false;
        }

        if (!ProfiledTools.TryGetValue(protocolTool.Name, out IReadOnlySet<string>? expectedProperties))
        {
            reason = DiagnosticCapabilityExclusionReason.NotProfiled;
            return false;
        }

        if (!ExtractPropertyNames(protocolTool.InputSchema).SetEquals(expectedProperties))
        {
            reason = DiagnosticCapabilityExclusionReason.SchemaDrifted;
            return false;
        }

        reason = default;
        return true;
    }

    private static IReadOnlySet<string> Props(params string[] names) =>
        new HashSet<string>(names, StringComparer.Ordinal);

    private static IReadOnlySet<string> ExtractPropertyNames(JsonElement inputSchema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (inputSchema.ValueKind != JsonValueKind.Object ||
            !inputSchema.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return names;
        }

        foreach (JsonProperty property in properties.EnumerateObject())
        {
            names.Add(property.Name);
        }

        return names;
    }
}
