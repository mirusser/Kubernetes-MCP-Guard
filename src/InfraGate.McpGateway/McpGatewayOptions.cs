namespace InfraGate.McpGateway;

public sealed record McpGatewayOptions(
    string BearerToken,
    string DownstreamProject,
    string GuardAuditRoot,
    string WorkingDirectory)
{
    public const string DefaultUrl = "http://127.0.0.1:3001";

    public static McpGatewayOptions FromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable("INFRA_GATE_GATEWAY_BEARER_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("INFRA_GATE_GATEWAY_BEARER_TOKEN is required.");
        }

        var workingDirectory = Directory.GetCurrentDirectory();
        var downstreamProject =
            Environment.GetEnvironmentVariable("INFRA_GATE_DOWNSTREAM_PROJECT") ??
            Path.Combine(workingDirectory, "src", "InfraGate.McpServer", "InfraGate.McpServer.csproj");
        var auditRoot =
            Environment.GetEnvironmentVariable("INFRA_GATE_GUARD_AUDIT_ROOT") ??
            Path.Combine(workingDirectory, ".mcp-guardrails");

        return new McpGatewayOptions(token, downstreamProject, auditRoot, workingDirectory);
    }
}
