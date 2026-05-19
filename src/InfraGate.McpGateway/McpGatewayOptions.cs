using System.Globalization;
using InfraGate.Approvals;
using InfraGate.McpGateway.Auth;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway;

public sealed record McpGatewayOptions(
    GatewayAuthOptions Auth,
    string DownstreamProject,
    string GuardAuditRoot,
    string WorkingDirectory,
    string ApprovalRoot,
    string? ApprovalBaseUrl,
    TimeSpan ApprovalChallengeTtl,
    string? DownstreamAssembly = null,
    RuntimeMode RuntimeMode = RuntimeMode.Development,
    bool IsGuardAuditRootExplicit = true,
    bool IsApprovalRootExplicit = true)
{
    public const string DefaultUrl = McpGatewayConventions.DefaultUrl;
    public static readonly TimeSpan DefaultApprovalChallengeTtl = TimeSpan.FromMinutes(15);
    private static readonly IReadOnlySet<string> DeniedApprovalRootNames =
        new HashSet<string>([ApprovalConventions.Storage.DefaultRootDirectory], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> DeniedGuardAuditRootNames =
        new HashSet<string>([McpGatewayConventions.Paths.DefaultGuardAuditRootDirectory], StringComparer.Ordinal);

    public static McpGatewayOptions FromEnvironment()
    {
        var auth = GatewayAuthOptions.FromEnvironment();
        RuntimeMode runtimeMode = RuntimeModeResolver.FromEnvironment();
        string workingDirectory = Directory.GetCurrentDirectory();
        string downstreamProject =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.DownstreamProject) ??
            Path.Combine(
                workingDirectory,
                McpGatewayConventions.Paths.SourceDirectory,
                McpGatewayConventions.Paths.DefaultDownstreamProjectDirectory,
                McpGatewayConventions.Paths.DefaultDownstreamProjectFileName);
        string? downstreamAssembly =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.DownstreamAssembly);
        string? auditRootValue =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.GuardAuditRoot);
        bool isGuardAuditRootExplicit = !string.IsNullOrWhiteSpace(auditRootValue);
        string auditRoot = auditRootValue ??
            Path.Combine(workingDirectory, McpGatewayConventions.Paths.DefaultGuardAuditRootDirectory);
        string? approvalRootValue =
            Environment.GetEnvironmentVariable(ApprovalConventions.EnvironmentVariables.ApprovalRoot);
        bool isApprovalRootExplicit = !string.IsNullOrWhiteSpace(approvalRootValue);
        string approvalRoot = approvalRootValue ??
            Path.Combine(workingDirectory, ApprovalConventions.Storage.DefaultRootDirectory);
        string? approvalBaseUrl = Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl);
        TimeSpan approvalChallengeTtl = ParseTimeSpanSeconds(
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
            downstreamAssembly,
            runtimeMode,
            isGuardAuditRootExplicit,
            isApprovalRootExplicit);
    }

    public static McpGatewayOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var auth = GatewayAuthOptions.FromConfiguration(configuration);
        RuntimeMode runtimeMode = RuntimeModeResolver.FromConfiguration(configuration);
        string workingDirectory = Directory.GetCurrentDirectory();
        string downstreamProject = GetConfigurationValue(
                configuration,
                McpGatewayConventions.EnvironmentVariables.DownstreamProject,
                McpGatewayConventions.ConfigurationKeys.DownstreamProject) ??
            Path.Combine(
                workingDirectory,
                McpGatewayConventions.Paths.SourceDirectory,
                McpGatewayConventions.Paths.DefaultDownstreamProjectDirectory,
                McpGatewayConventions.Paths.DefaultDownstreamProjectFileName);
        string? downstreamAssembly = GetConfigurationValue(
            configuration,
            McpGatewayConventions.EnvironmentVariables.DownstreamAssembly,
            McpGatewayConventions.ConfigurationKeys.DownstreamAssembly);
        string? auditRootValue = GetConfigurationValue(
            configuration,
            McpGatewayConventions.EnvironmentVariables.GuardAuditRoot,
            McpGatewayConventions.ConfigurationKeys.GuardAuditRoot);
        bool isGuardAuditRootExplicit = !string.IsNullOrWhiteSpace(auditRootValue);
        string auditRoot = auditRootValue ??
            Path.Combine(workingDirectory, McpGatewayConventions.Paths.DefaultGuardAuditRootDirectory);
        string? approvalRootValue = GetConfigurationValue(
            configuration,
            ApprovalConventions.EnvironmentVariables.ApprovalRoot,
            McpGatewayConventions.ConfigurationKeys.ApprovalRoot);
        bool isApprovalRootExplicit = !string.IsNullOrWhiteSpace(approvalRootValue);
        string approvalRoot = approvalRootValue ??
            Path.Combine(workingDirectory, ApprovalConventions.Storage.DefaultRootDirectory);
        string? approvalBaseUrl = GetConfigurationValue(
            configuration,
            McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl,
            McpGatewayConventions.ConfigurationKeys.ApprovalBaseUrl);
        TimeSpan approvalChallengeTtl = ParseTimeSpanSeconds(
            GetConfigurationValue(
                configuration,
                McpGatewayConventions.EnvironmentVariables.ApprovalChallengeTtlSeconds,
                McpGatewayConventions.ConfigurationKeys.ApprovalChallengeTtlSeconds),
            DefaultApprovalChallengeTtl);

        return new McpGatewayOptions(
            auth,
            downstreamProject,
            auditRoot,
            workingDirectory,
            approvalRoot,
            approvalBaseUrl,
            approvalChallengeTtl,
            downstreamAssembly,
            runtimeMode,
            isGuardAuditRootExplicit,
            isApprovalRootExplicit);
    }

    public void ValidateProductionSafety()
    {
        if (RuntimeMode != RuntimeMode.Production)
        {
            return;
        }

        if (!Auth.OAuthRequireHttpsMetadata)
        {
            throw new InvalidOperationException(
                $"{GatewayAuthConventions.EnvironmentVariables.OAuthRequireHttpsMetadata} must be true in Production mode.");
        }

        ProductionSafetyValidator.RequireHttpsNonLoopbackUri(
            Auth.OAuthAuthority,
            GatewayAuthConventions.EnvironmentVariables.OAuthAuthority);
        if (!string.IsNullOrWhiteSpace(Auth.OAuthMetadataAddress))
        {
            ProductionSafetyValidator.RequireHttpsNonLoopbackUri(
                Auth.OAuthMetadataAddress,
                GatewayAuthConventions.EnvironmentVariables.OAuthMetadataAddress);
        }

        ProductionSafetyValidator.RequireHttpsNonLoopbackUri(
            Auth.OAuthResource,
            GatewayAuthConventions.EnvironmentVariables.OAuthResource);
        ProductionSafetyValidator.RequireHttpsNonLoopbackUri(
            Auth.ApprovalAuthorizationEndpoint,
            GatewayAuthConventions.EnvironmentVariables.ApprovalOAuthAuthorizationEndpoint);
        ProductionSafetyValidator.RequireHttpsNonLoopbackUri(
            Auth.ApprovalTokenEndpoint,
            GatewayAuthConventions.EnvironmentVariables.ApprovalOAuthTokenEndpoint);
        ProductionSafetyValidator.RequireHttpsNonLoopbackUri(
            ApprovalBaseUrl,
            McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl);

        ProductionSafetyValidator.RequirePersistentDirectory(
            ApprovalRoot,
            ApprovalConventions.EnvironmentVariables.ApprovalRoot,
            IsApprovalRootExplicit,
            DeniedApprovalRootNames);
        ProductionSafetyValidator.RequirePersistentDirectory(
            GuardAuditRoot,
            McpGatewayConventions.EnvironmentVariables.GuardAuditRoot,
            IsGuardAuditRootExplicit,
            DeniedGuardAuditRootNames);
    }

    private static TimeSpan ParseTimeSpanSeconds(string? value, TimeSpan defaultValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : TimeSpan.FromSeconds(double.Parse(value, CultureInfo.InvariantCulture));
    }

    private static string? GetConfigurationValue(
        IConfiguration configuration,
        string environmentVariable,
        string configurationKey)
    {
        string? environmentValue = configuration[environmentVariable];
        return !string.IsNullOrWhiteSpace(environmentValue)
            ? environmentValue
            : configuration[configurationKey];
    }
}
