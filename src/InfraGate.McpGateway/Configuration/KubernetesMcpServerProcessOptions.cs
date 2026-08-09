namespace InfraGate.McpGateway;

// Descriptor for the secondary, read-only-only kubernetes-mcp-server downstream
// process (see docs/adr for the decision record). Deliberately a sibling type,
// not an extension of McpGatewayOptions: ValidateProductionSafety()'s
// DownstreamAuth.Required==true production gate is scoped to the primary
// downstream and must never apply to this always-unauthenticated descriptor.
public sealed record class KubernetesMcpServerProcessOptions(
    string Command,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory)
{
    // Always false: this downstream only understands stock MCP stdio, not
    // InfraGate's private bootstrap-auth protocol. Trust relies on trusted
    // launch + process containment instead (see Gateway README security
    // priority order, where the downstream token already ranks last).
    public const bool AuthRequired = false;

    public static KubernetesMcpServerProcessOptions? FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(
            McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection);
        string? command = section[McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerCommandKey];
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string[] arguments = section
            .GetSection(McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerArgumentsKey)
            .Get<string[]>() ?? [];
        string workingDirectory =
            section[McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerWorkingDirectoryKey]
            ?? Directory.GetCurrentDirectory();

        return new KubernetesMcpServerProcessOptions(command, arguments, workingDirectory);
    }
}
