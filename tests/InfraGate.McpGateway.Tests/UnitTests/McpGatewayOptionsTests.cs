using Microsoft.Extensions.Logging.Abstractions;
using InfraGate.Approvals;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.DownstreamAuth;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class McpGatewayOptionsTests
{
    private const string OAuthAuthority = "https://issuer.example.com";
    private const string DownstreamAssembly = "/app/server/InfraGate.McpServer.dll";
    private const string DownstreamProject = "server.csproj";
    private const string GuardAuditRoot = "guardrails";
    private const string WorkingDirectory = "/repo";
    private const string ApprovalRoot = "approvals";
    private const string GatewayResource = "https://gateway.example.com/mcp";
    private const string ApprovalBaseUrl = "https://gateway.example.com";
    private const string DownstreamAuthority = "https://idp.example.com";
    private const string DownstreamClientId = "infra-gate-gateway";

    [Fact]
    public void FromConfiguration_UsesDownstreamAssembly_WhenSet()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (McpGatewayConventions.ConfigurationKeys.DownstreamAssembly, DownstreamAssembly));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.Equal(DownstreamAssembly, options.DownstreamAssembly);
    }

    [Fact]
    public void FromConfiguration_LeavesDownstreamAssemblyNull_WhenUnset()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.Null(options.DownstreamAssembly);
    }

    [Fact]
    public void FromConfiguration_WithSmtpEnableSslFalse_ConfiguresSmtpWithoutTls()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (McpGatewayConventions.ConfigurationKeys.SmtpHost, "mailpit"),
            (McpGatewayConventions.ConfigurationKeys.SmtpFrom, "infragate@example.local"),
            (McpGatewayConventions.ConfigurationKeys.SmtpEnableSsl, "false"));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.NotNull(options.Smtp);
        Assert.False(options.Smtp.EnableSsl);
    }

    [Fact]
    public void FromConfiguration_WithSmtpHostOnly_ConfiguresSmtpWithDefaultPort()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (McpGatewayConventions.ConfigurationKeys.SmtpHost, "mailpit"));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.NotNull(options.Smtp);
        Assert.Equal("mailpit", options.Smtp.Host);
        Assert.Equal(25, options.Smtp.Port);
        Assert.True(options.Smtp.EnableSsl);
    }

    [Fact]
    public void FromConfiguration_WithSmtpFromOnly_ConfiguresSmtp()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (McpGatewayConventions.ConfigurationKeys.SmtpFrom, "infragate@example.local"));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.NotNull(options.Smtp);
        Assert.Equal("infragate@example.local", options.Smtp.FromAddress);
    }

    [Fact]
    public void FromConfiguration_WithSmtpPort_ConfiguresSmtpWithCustomPort()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (McpGatewayConventions.ConfigurationKeys.SmtpHost, "mailpit"),
            (McpGatewayConventions.ConfigurationKeys.SmtpFrom, "infragate@example.local"),
            (McpGatewayConventions.ConfigurationKeys.SmtpPort, "2525"));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.NotNull(options.Smtp);
        Assert.Equal(2525, options.Smtp.Port);
    }

    [Fact]
    public void FromConfiguration_WithSmtpHostAndUser_CreatesSmtpOptions()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (McpGatewayConventions.ConfigurationKeys.SmtpHost, "mailpit"),
            (McpGatewayConventions.ConfigurationKeys.SmtpFrom, "infragate@example.local"),
            (McpGatewayConventions.ConfigurationKeys.SmtpUser, "user"));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.NotNull(options.Smtp);
        Assert.Equal("user", options.Smtp.Username);
    }

    [Fact]
    public void FromConfiguration_NoSmtpHostOrFrom_LeavesSmtpNull()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.Null(options.Smtp);
    }

    [Fact]
    public void FromConfiguration_InfraGateEnvironmentOverDotNet_UsesDevelopment()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment, RuntimeSafetyConventions.EnvironmentValues.Production));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.Equal(RuntimeMode.Development, options.RuntimeMode);
    }

    [Fact]
    public void FromConfiguration_WithUnsupportedInfraGateEnvironment_Throws()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment, "Staging"));

        Assert.Throws<InvalidOperationException>(() => McpGatewayOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void FromConfiguration_UsesGeneratedAppSettingsValues()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (McpGatewayConventions.ConfigurationKeys.DownstreamAssembly, DownstreamAssembly),
            (McpGatewayConventions.ConfigurationKeys.GuardAuditRoot, GuardAuditRoot),
            (McpGatewayConventions.ConfigurationKeys.ApprovalRoot, ApprovalRoot),
            (McpGatewayConventions.ConfigurationKeys.ApprovalBaseUrl, ApprovalBaseUrl),
            (RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment,
                RuntimeSafetyConventions.EnvironmentValues.Production));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.Equal(DownstreamAssembly, options.DownstreamAssembly);
        Assert.Equal(GuardAuditRoot, options.GuardAuditRoot);
        Assert.Equal(ApprovalRoot, options.ApprovalRoot);
        Assert.Equal(ApprovalBaseUrl, options.ApprovalBaseUrl);
        Assert.Equal(RuntimeMode.Production, options.RuntimeMode);
        Assert.True(options.IsGuardAuditRootExplicit);
        Assert.True(options.IsApprovalRootExplicit);
    }

    [Fact]
    public void FromConfiguration_BindsFromGatewayAndApprovalSections()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (McpGatewayConventions.ConfigurationKeys.DownstreamAssembly, "/app/server.dll"),
            (McpGatewayConventions.ConfigurationKeys.ApprovalBaseUrl, "https://gateway.example.com"));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.Equal("/app/server.dll", options.DownstreamAssembly);
        Assert.Equal("https://gateway.example.com", options.ApprovalBaseUrl);
    }

    [Fact]
    public void CreateTransportOptions_UsesProjectRunArguments_WhenAssemblyUnset()
    {
        var client = new DownstreamMcpClient(CreateOptions(), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.Equal(McpGatewayConventions.DownstreamProcess.Command, transportOptions.Command);
        Assert.Equal(
            [
                McpGatewayConventions.DownstreamProcess.RunArgument,
                McpGatewayConventions.DownstreamProcess.ProjectArgument,
                DownstreamProject
            ],
            transportOptions.Arguments);
        Assert.Equal(WorkingDirectory, transportOptions.WorkingDirectory);
    }

    [Fact]
    public void CreateTransportOptions_UsesAssemblyArgument_WhenAssemblySet()
    {
        var client = new DownstreamMcpClient(CreateOptions(DownstreamAssembly), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.Equal(McpGatewayConventions.DownstreamProcess.Command, transportOptions.Command);
        Assert.Equal([DownstreamAssembly], transportOptions.Arguments);
        Assert.Equal(WorkingDirectory, transportOptions.WorkingDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void CreateTransportOptions_FallsBackToProject_WhenAssemblyIsEmptyOrWhitespace(string assembly)
    {
        var client = new DownstreamMcpClient(CreateOptions(assembly), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.Equal(
            [
                McpGatewayConventions.DownstreamProcess.RunArgument,
                McpGatewayConventions.DownstreamProcess.ProjectArgument,
                DownstreamProject
            ],
            transportOptions.Arguments);
    }

    [Fact]
    public void ValidateProductionSafety_WithDevelopmentMode_AllowsLocalSettings()
    {
        var options = CreateOptions();

        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateProductionSafety_WithValidExternalSettings_AllowsStartup()
    {
        var configuration = BuildProductionConfig();
        var options = McpGatewayOptions.FromConfiguration(configuration);

        var exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void ProductionMode_WithHttpMetadata_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthRequireHttpsMetadata, "false"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(GatewayAuthConventions.EnvironmentVariables.OAuthRequireHttpsMetadata, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithHttpMetadataAddress_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthMetadataAddress, "http://issuer.example.com/.well-known/openid-configuration"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(GatewayAuthConventions.EnvironmentVariables.OAuthMetadataAddress, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithLocalhostOAuthResource_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthResource, "https://127.0.0.1:3001/mcp"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(GatewayAuthConventions.EnvironmentVariables.OAuthResource, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithoutApprovalBaseUrl_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (McpGatewayConventions.ConfigurationKeys.ApprovalBaseUrl, null));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithHttpApprovalBaseUrl_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (McpGatewayConventions.ConfigurationKeys.ApprovalBaseUrl, "http://gateway.example.com"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithTempApprovalRoot_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (McpGatewayConventions.ConfigurationKeys.ApprovalRoot, Path.Combine(Path.GetTempPath(), "infra-gate-approvals")));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(ApprovalConventions.EnvironmentVariables.ApprovalRoot, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithDefaultGuardAuditRoot_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (McpGatewayConventions.ConfigurationKeys.GuardAuditRoot, null));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(McpGatewayConventions.EnvironmentVariables.GuardAuditRoot, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithDownstreamAuthRequired_False_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (DownstreamAuthConventions.ConfigurationKeys.Required, "false"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.Required, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithTokenIntrospectionDisabled_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (GatewayAuthConventions.ConfigurationKeys.TokenIntrospectionEnabled, "false"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(GatewayAuthConventions.EnvironmentVariables.TokenIntrospectionEnabled, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithLongMaxAcceptedTokenLifetime_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (GatewayAuthConventions.ConfigurationKeys.MaxAcceptedAccessTokenLifetimeSeconds, "301"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(GatewayAuthConventions.EnvironmentVariables.MaxAcceptedAccessTokenLifetimeSeconds, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithValidDownstreamAuth_AllowsStartup()
    {
        var configuration = BuildProductionConfig();
        var options = McpGatewayOptions.FromConfiguration(configuration);

        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateProductionSafety_WithHttpDownstreamAuthority_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (DownstreamAuthConventions.ConfigurationKeys.Authority, "http://idp.example.com"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.Authority, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithLoopbackDownstreamAuthority_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (DownstreamAuthConventions.ConfigurationKeys.Authority, "https://127.0.0.1:8443/realms/test"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.Authority, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithHttpDownstreamMetadataAddress_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (DownstreamAuthConventions.ConfigurationKeys.MetadataAddress, "http://idp.example.com/.well-known/openid-configuration"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.MetadataAddress, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithDownstreamRequireHttpsMetadataFalse_RefusesStartup()
    {
        var configuration = BuildProductionConfig(
            (DownstreamAuthConventions.ConfigurationKeys.RequireHttpsMetadata, "false"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.RequireHttpsMetadata, exception.Message);
    }

    [Fact]
    public void DevelopmentMode_WithDownstreamAuthRequired_False_AllowsStartup()
    {
        var configuration = BuildConfig(
            (RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (DownstreamAuthConventions.ConfigurationKeys.Required, "false"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void FromConfiguration_WithDownstreamAuthSectionAbsent_LeavesDownstreamAuthNull()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.Null(options.DownstreamAuth);
    }

    [Fact]
    public void FromConfiguration_WithDownstreamAuthSectionPresent_NoRequiredKey_DefaultsToRequired()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (DownstreamAuthConventions.ConfigurationKeys.Authority, DownstreamAuthority));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.NotNull(options.DownstreamAuth);
        Assert.True(options.DownstreamAuth.Required);
    }

    [Fact]
    public void FromConfiguration_PopulatesDownstreamAuth_FromSection()
    {
        var configuration = BuildConfig(
            (GatewayAuthConventions.ConfigurationKeys.OAuthAuthority, OAuthAuthority),
            (DownstreamAuthConventions.ConfigurationKeys.Required, "true"),
            (DownstreamAuthConventions.ConfigurationKeys.Authority, DownstreamAuthority),
            (DownstreamAuthConventions.ConfigurationKeys.GatewayClientId, DownstreamClientId));

        var options = McpGatewayOptions.FromConfiguration(configuration);

        Assert.NotNull(options.DownstreamAuth);
        Assert.True(options.DownstreamAuth.Required);
        Assert.Equal(DownstreamAuthority, options.DownstreamAuth.Authority);
        Assert.Equal(DownstreamClientId, options.DownstreamAuth.GatewayClientId);
    }

    private static McpGatewayOptions CreateOptions(string? downstreamAssembly = null) =>
        new(
            new GatewayAuthOptions(OAuthAuthority),
            DownstreamProject,
            GuardAuditRoot,
            WorkingDirectory,
            ApprovalRoot,
            ApprovalBaseUrl: null,
            McpGatewayOptions.DefaultApprovalChallengeTtl,
            downstreamAssembly);

    private static IConfiguration BuildConfig(params (string Key, string? Value)[] entries)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => e.Value))
            .Build();
    }

    private static IConfiguration BuildProductionConfig(params (string Key, string? Value)[] overrides)
    {
        var entries = new Dictionary<string, string?>
        {
            [RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment] =
                RuntimeSafetyConventions.EnvironmentValues.Production,
            [GatewayAuthConventions.ConfigurationKeys.OAuthAuthority] = OAuthAuthority,
            [GatewayAuthConventions.ConfigurationKeys.OAuthMetadataAddress] = null,
            [GatewayAuthConventions.ConfigurationKeys.OAuthResource] = GatewayResource,
            [GatewayAuthConventions.ConfigurationKeys.OAuthScope] = GatewayAuthConventions.DefaultOAuthScope,
            [GatewayAuthConventions.ConfigurationKeys.OAuthRequireHttpsMetadata] = "true",
            [GatewayAuthConventions.ConfigurationKeys.ApprovalOAuthAuthorizationEndpoint] = OAuthAuthority + "/authorize",
            [GatewayAuthConventions.ConfigurationKeys.ApprovalOAuthTokenEndpoint] = OAuthAuthority + "/token",
            [GatewayAuthConventions.ConfigurationKeys.TokenIntrospectionEnabled] = "true",
            [GatewayAuthConventions.ConfigurationKeys.TokenIntrospectionClientId] = "gateway-resource-server",
            [GatewayAuthConventions.ConfigurationKeys.TokenIntrospectionClientSecret] = "secret-placeholder",
            [GatewayAuthConventions.ConfigurationKeys.MaxAcceptedAccessTokenLifetimeSeconds] = "300",
            [McpGatewayConventions.ConfigurationKeys.ApprovalBaseUrl] = ApprovalBaseUrl,
            [McpGatewayConventions.ConfigurationKeys.GuardAuditRoot] = ProductionPath("guardrails"),
            [McpGatewayConventions.ConfigurationKeys.ApprovalRoot] = ProductionPath("approvals"),
            [DownstreamAuthConventions.ConfigurationKeys.Required] = "true",
            [DownstreamAuthConventions.ConfigurationKeys.Authority] = DownstreamAuthority,
            [DownstreamAuthConventions.ConfigurationKeys.GatewayClientId] = DownstreamClientId,
        };

        foreach (var (key, value) in overrides)
        {
            entries[key] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries)
            .Build();
    }

    private static string ProductionPath(string directoryName)
    {
        string root = Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? Path.DirectorySeparatorChar.ToString();

        return Path.Combine(root, "var", "lib", "infra-gate-tests", directoryName);
    }
}
