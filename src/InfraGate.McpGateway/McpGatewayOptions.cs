using System.Globalization;
using InfraGate.Approvals;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway;

public sealed record McpGatewayOptions(
    GatewayAuthOptions Auth,
    string DownstreamProject,
    string GuardAuditRoot,
    string WorkingDirectory,
    string ApprovalRoot,
    string? ApprovalBaseUrl,
    TimeSpan ApprovalChallengeTtl,
    string? DownstreamAssembly = null)
{
    public const string DefaultUrl = McpGatewayConventions.DefaultUrl;
    public static readonly TimeSpan DefaultApprovalChallengeTtl = TimeSpan.FromMinutes(15);

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
        var downstreamAssembly =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.DownstreamAssembly);
        var auditRoot =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.GuardAuditRoot) ??
            Path.Combine(workingDirectory, McpGatewayConventions.Paths.DefaultGuardAuditRootDirectory);
        var approvalRoot =
            Environment.GetEnvironmentVariable(ApprovalConventions.EnvironmentVariables.ApprovalRoot) ??
            Path.Combine(workingDirectory, ApprovalConventions.Storage.DefaultRootDirectory);
        var approvalBaseUrl = Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl);
        var approvalChallengeTtl = ParseTimeSpanSeconds(
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.ApprovalChallengeTtlSeconds),
            DefaultApprovalChallengeTtl);

        return new McpGatewayOptions(
            auth,
            downstreamProject,
            auditRoot,
            workingDirectory,
            approvalRoot,
            approvalBaseUrl,
            approvalChallengeTtl,
            downstreamAssembly);
    }

    private static TimeSpan ParseTimeSpanSeconds(string? value, TimeSpan defaultValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : TimeSpan.FromSeconds(double.Parse(value, CultureInfo.InvariantCulture));
    }
}
