using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway;

public sealed record McpGatewayOptions(
    GatewayAuthOptions Auth,
    string DownstreamProject,
    string GuardAuditRoot,
    string WorkingDirectory)
{
    public const string DefaultUrl = McpGatewayConventions.DefaultUrl;

    public static McpGatewayOptions FromEnvironment()
    {
        var auth = GatewayAuthOptions.FromEnvironment();
        var workingDirectory = Directory.GetCurrentDirectory();
        var downstreamProject =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.DownstreamProject) ??
            Path.Combine(
                workingDirectory,
                McpGatewayConventions.Paths.SourceDirectory,
                McpGatewayConventions.Paths.DefaultDownstreamProjectDirectory,
                McpGatewayConventions.Paths.DefaultDownstreamProjectFileName);
        var auditRoot =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.GuardAuditRoot) ??
            Path.Combine(workingDirectory, McpGatewayConventions.Paths.DefaultGuardAuditRootDirectory);

        return new McpGatewayOptions(
            auth,
            downstreamProject,
            auditRoot,
            workingDirectory);
    }
}
