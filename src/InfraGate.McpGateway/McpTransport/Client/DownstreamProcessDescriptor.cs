namespace InfraGate.McpGateway;

// Per-instance transport data for a downstream stdio MCP process. Generalizes what was
// previously hardcoded onto McpGatewayOptions/McpGatewayConventions.DownstreamProcess so
// DownstreamMcpClient can spawn either the primary (dotnet InfraGate.McpServer) or the
// secondary (kubernetes-mcp-server Go binary) downstream from the same code.
internal sealed record class DownstreamProcessDescriptor(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    bool AuthRequired,
    IReadOnlySet<string> AllowedEnvironmentVariables)
{
    public static DownstreamProcessDescriptor ForPrimary(McpGatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string[] arguments = string.IsNullOrWhiteSpace(options.DownstreamAssembly)
            ? [
                McpGatewayConventions.DownstreamProcess.RunArgument,
                McpGatewayConventions.DownstreamProcess.ProjectArgument,
                options.DownstreamProject
            ]
            : [options.DownstreamAssembly];

        return new DownstreamProcessDescriptor(
            McpGatewayConventions.DownstreamProcess.Name,
            McpGatewayConventions.DownstreamProcess.Command,
            arguments,
            options.WorkingDirectory,
            options.DownstreamAuth?.Required ?? false,
            McpGatewayConventions.DownstreamProcess.AllowedEnvironmentVariables);
    }

    public static DownstreamProcessDescriptor ForKubernetesMcpServer(KubernetesMcpServerProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new DownstreamProcessDescriptor(
            McpGatewayConventions.SecondaryDownstream.Name,
            options.Command,
            options.Arguments,
            options.WorkingDirectory,
            KubernetesMcpServerProcessOptions.AuthRequired,
            McpGatewayConventions.SecondaryDownstream.AllowedEnvironmentVariables);
    }
}
