using System.Globalization;
using InfraGate.Approvals;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Email;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway;

public sealed record class McpGatewayOptions(
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
    bool IsApprovalRootExplicit = true,
    DownstreamAuthOptions? DownstreamAuth = null,
    string OperatorGroup = McpGatewayConventions.DefaultOperatorGroup,
    string? OperatorEmail = null,
    SmtpApprovalEmailOptions? Smtp = null)
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
        var downstreamAuth = DownstreamAuthOptions.FromEnvironment();
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
        string operatorGroup =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.OperatorGroup) ??
            McpGatewayConventions.DefaultOperatorGroup;
        string? operatorEmail = Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.OperatorEmail);
        var smtp = CreateSmtpOptions(
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.SmtpHost),
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.SmtpPort),
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.SmtpFrom),
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.SmtpUser),
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.SmtpPassword));

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
            isApprovalRootExplicit,
            downstreamAuth,
            operatorGroup,
            operatorEmail,
            smtp);
    }

    public static McpGatewayOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var auth = GatewayAuthOptions.FromConfiguration(configuration);
        var downstreamAuth = configuration
            .GetSection("InfraGate:DownstreamAuth")
            .Get<DownstreamAuthOptions>();
        RuntimeMode runtimeMode = RuntimeModeResolver.FromConfiguration(configuration);
        string workingDirectory = Directory.GetCurrentDirectory();

        var gatewaySettings = configuration
            .GetSection("InfraGate:Gateway")
            .Get<InfraGateGatewaySettings>();
        var approvalSettings = configuration
            .GetSection("InfraGate:Approval")
            .Get<InfraGateApprovalSettings>();

        string downstreamProject = gatewaySettings?.DownstreamProject ??
            Path.Combine(
                workingDirectory,
                McpGatewayConventions.Paths.SourceDirectory,
                McpGatewayConventions.Paths.DefaultDownstreamProjectDirectory,
                McpGatewayConventions.Paths.DefaultDownstreamProjectFileName);
        string? downstreamAssembly = gatewaySettings?.DownstreamAssembly;
        string? auditRootValue = gatewaySettings?.GuardAuditRoot;
        bool isGuardAuditRootExplicit = !string.IsNullOrWhiteSpace(auditRootValue);
        string auditRoot = auditRootValue ??
            Path.Combine(workingDirectory, McpGatewayConventions.Paths.DefaultGuardAuditRootDirectory);
        string? approvalRootValue = approvalSettings?.Root;
        bool isApprovalRootExplicit = !string.IsNullOrWhiteSpace(approvalRootValue);
        string approvalRoot = approvalRootValue ??
            Path.Combine(workingDirectory, ApprovalConventions.Storage.DefaultRootDirectory);
        string? approvalBaseUrl = approvalSettings?.BaseUrl;
        TimeSpan approvalChallengeTtl = ParseTimeSpanSeconds(
            approvalSettings?.ChallengeTtlSeconds,
            DefaultApprovalChallengeTtl);
        string operatorGroup = approvalSettings?.OperatorGroup ?? McpGatewayConventions.DefaultOperatorGroup;
        string? operatorEmail = approvalSettings?.OperatorEmail;
        var smtp = CreateSmtpOptions(
            approvalSettings?.Smtp?.Host,
            approvalSettings?.Smtp?.Port,
            approvalSettings?.Smtp?.From,
            approvalSettings?.Smtp?.User,
            approvalSettings?.Smtp?.Password);

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
            isApprovalRootExplicit,
            downstreamAuth,
            operatorGroup,
            operatorEmail,
            smtp);
    }

    public void ValidateProductionSafety()
    {
        if (RuntimeMode != RuntimeMode.Production)
        {
            return;
        }

        var downstreamAuth = DownstreamAuth ?? new DownstreamAuthOptions();
        if (!downstreamAuth.Required)
        {
            throw new InvalidOperationException(
                $"{DownstreamAuthConventions.EnvironmentVariables.Required} must not be false in Production mode. " +
                $"Downstream authentication is required for production deployments.");
        }

        downstreamAuth.Validate();

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

    private static SmtpApprovalEmailOptions? CreateSmtpOptions(
        string? host,
        string? port,
        string? from,
        string? user,
        string? password)
    {
        if (string.IsNullOrWhiteSpace(host) &&
            string.IsNullOrWhiteSpace(from))
        {
            return null;
        }

        int parsedPort = string.IsNullOrWhiteSpace(port)
            ? SmtpApprovalEmailOptions.DefaultPort
            : int.Parse(port, CultureInfo.InvariantCulture);

        return new SmtpApprovalEmailOptions(
            host ?? string.Empty,
            parsedPort,
            from ?? string.Empty,
            user,
            password);
    }

}
