namespace InfraGate.McpGateway;

public sealed record McpGatewayOptions(
    string BearerToken,
    string DownstreamProject,
    string GuardAuditRoot,
    string WorkingDirectory)
{
    public const string DefaultUrl = McpGatewayConventions.DefaultUrl;

    public static McpGatewayOptions FromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.BearerToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"{McpGatewayConventions.EnvironmentVariables.BearerToken} is required.");
        }

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

        return new McpGatewayOptions(token, downstreamProject, auditRoot, workingDirectory);
    }
}
