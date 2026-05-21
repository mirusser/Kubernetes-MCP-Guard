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
    public void FromEnvironment_UsesDownstreamAssembly_WhenSet()
    {
        using var environment = EnvironmentVariableScope.Set(
            (GatewayAuthConventions.EnvironmentVariables.OAuthAuthority, OAuthAuthority),
            (McpGatewayConventions.EnvironmentVariables.DownstreamAssembly, DownstreamAssembly));

        var options = McpGatewayOptions.FromEnvironment();

        Assert.Equal(DownstreamAssembly, options.DownstreamAssembly);
    }

    [Fact]
    public void FromEnvironment_LeavesDownstreamAssemblyNull_WhenUnset()
    {
        using var environment = EnvironmentVariableScope.Set(
            (GatewayAuthConventions.EnvironmentVariables.OAuthAuthority, OAuthAuthority),
            (McpGatewayConventions.EnvironmentVariables.DownstreamAssembly, null));

        var options = McpGatewayOptions.FromEnvironment();

        Assert.Null(options.DownstreamAssembly);
    }

    [Fact]
    public void FromEnvironment_UsesInfraGateEnvironmentOverStandardEnvironment()
    {
        using var environment = EnvironmentVariableScope.Set(
            (GatewayAuthConventions.EnvironmentVariables.OAuthAuthority, OAuthAuthority),
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment, RuntimeSafetyConventions.EnvironmentValues.Production));

        var options = McpGatewayOptions.FromEnvironment();

        Assert.Equal(RuntimeMode.Development, options.RuntimeMode);
    }

    [Fact]
    public void FromEnvironment_WithUnsupportedInfraGateEnvironment_Throws()
    {
        using var environment = EnvironmentVariableScope.Set(
            (GatewayAuthConventions.EnvironmentVariables.OAuthAuthority, OAuthAuthority),
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, "Staging"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(McpGatewayOptions.FromEnvironment);

        Assert.Contains(RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, exception.Message);
    }

    [Fact]
    public void FromConfiguration_UsesGeneratedAppSettingsValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GatewayAuthConventions.ConfigurationKeys.OAuthAuthority] = OAuthAuthority,
                [McpGatewayConventions.ConfigurationKeys.DownstreamAssembly] = DownstreamAssembly,
                [McpGatewayConventions.ConfigurationKeys.GuardAuditRoot] = GuardAuditRoot,
                [McpGatewayConventions.ConfigurationKeys.ApprovalRoot] = ApprovalRoot,
                [McpGatewayConventions.ConfigurationKeys.ApprovalBaseUrl] = ApprovalBaseUrl,
                [RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment] =
                    RuntimeSafetyConventions.EnvironmentValues.Production
            })
            .Build();

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
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GatewayAuthConventions.ConfigurationKeys.OAuthAuthority] = OAuthAuthority,
                [McpGatewayConventions.ConfigurationKeys.DownstreamAssembly] = "/app/server.dll",
                [McpGatewayConventions.ConfigurationKeys.ApprovalBaseUrl] = "https://gateway.example.com"
            })
            .Build();

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
        using var environment = SetProductionEnvironment();

        var options = McpGatewayOptions.FromEnvironment();
        var exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void ProductionMode_WithHttpMetadata_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (GatewayAuthConventions.EnvironmentVariables.OAuthRequireHttpsMetadata, "false"));

        var options = McpGatewayOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(GatewayAuthConventions.EnvironmentVariables.OAuthRequireHttpsMetadata, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithHttpMetadataAddress_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (GatewayAuthConventions.EnvironmentVariables.OAuthMetadataAddress, "http://issuer.example.com/.well-known/openid-configuration"));

        var options = McpGatewayOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(GatewayAuthConventions.EnvironmentVariables.OAuthMetadataAddress, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithLocalhostOAuthResource_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (GatewayAuthConventions.EnvironmentVariables.OAuthResource, "https://127.0.0.1:3001/mcp"));

        var options = McpGatewayOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(GatewayAuthConventions.EnvironmentVariables.OAuthResource, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithoutApprovalBaseUrl_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl, null));

        var options = McpGatewayOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithHttpApprovalBaseUrl_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl, "http://gateway.example.com"));

        var options = McpGatewayOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithTempApprovalRoot_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (ApprovalConventions.EnvironmentVariables.ApprovalRoot, Path.Combine(Path.GetTempPath(), "infra-gate-approvals")));

        var options = McpGatewayOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(ApprovalConventions.EnvironmentVariables.ApprovalRoot, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithDefaultGuardAuditRoot_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (McpGatewayConventions.EnvironmentVariables.GuardAuditRoot, null));

        var options = McpGatewayOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(McpGatewayConventions.EnvironmentVariables.GuardAuditRoot, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithDownstreamAuthRequired_False_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (DownstreamAuthConventions.EnvironmentVariables.Required, "false"));

        var options = McpGatewayOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.Required, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithValidDownstreamAuth_AllowsStartup()
    {
        using var environment = SetProductionEnvironment();

        var options = McpGatewayOptions.FromEnvironment();
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void DevelopmentMode_WithDownstreamAuthRequired_False_AllowsStartup()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (GatewayAuthConventions.EnvironmentVariables.OAuthAuthority, OAuthAuthority),
            (DownstreamAuthConventions.EnvironmentVariables.Required, "false"));

        var options = McpGatewayOptions.FromEnvironment();
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void FromEnvironment_WithDownstreamAuthRequired_Absent_DefaultsToRequired()
    {
        using var environment = EnvironmentVariableScope.Set(
            (GatewayAuthConventions.EnvironmentVariables.OAuthAuthority, OAuthAuthority),
            (DownstreamAuthConventions.EnvironmentVariables.Required, null));

        var options = McpGatewayOptions.FromEnvironment();

        Assert.True(options.DownstreamAuth?.Required);
    }

    [Fact]
    public void FromEnvironment_PopulatesDownstreamAuth_FromEnvironment()
    {
        using var environment = EnvironmentVariableScope.Set(
            (GatewayAuthConventions.EnvironmentVariables.OAuthAuthority, OAuthAuthority),
            (DownstreamAuthConventions.EnvironmentVariables.Required, "true"),
            (DownstreamAuthConventions.EnvironmentVariables.Authority, DownstreamAuthority),
            (DownstreamAuthConventions.EnvironmentVariables.GatewayClientId, DownstreamClientId));

        var options = McpGatewayOptions.FromEnvironment();

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

    private static EnvironmentVariableScope SetProductionEnvironment(params (string Name, string? Value)[] overrides)
    {
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment] =
                RuntimeSafetyConventions.EnvironmentValues.Production,
            [GatewayAuthConventions.EnvironmentVariables.OAuthAuthority] = OAuthAuthority,
            [GatewayAuthConventions.EnvironmentVariables.OAuthMetadataAddress] = null,
            [GatewayAuthConventions.EnvironmentVariables.OAuthResource] = GatewayResource,
            [GatewayAuthConventions.EnvironmentVariables.OAuthScope] = GatewayAuthConventions.DefaultOAuthScope,
            [GatewayAuthConventions.EnvironmentVariables.OAuthRequireHttpsMetadata] = "true",
            [GatewayAuthConventions.EnvironmentVariables.ApprovalOAuthAuthorizationEndpoint] = OAuthAuthority + "/authorize",
            [GatewayAuthConventions.EnvironmentVariables.ApprovalOAuthTokenEndpoint] = OAuthAuthority + "/token",
            [McpGatewayConventions.EnvironmentVariables.ApprovalBaseUrl] = ApprovalBaseUrl,
            [McpGatewayConventions.EnvironmentVariables.GuardAuditRoot] = ProductionPath("guardrails"),
            [ApprovalConventions.EnvironmentVariables.ApprovalRoot] = ProductionPath("approvals"),
            [DownstreamAuthConventions.EnvironmentVariables.Required] = "true",
            [DownstreamAuthConventions.EnvironmentVariables.Authority] = DownstreamAuthority,
            [DownstreamAuthConventions.EnvironmentVariables.GatewayClientId] = DownstreamClientId
        };

        foreach (var item in overrides)
        {
            variables[item.Name] = item.Value;
        }

        return EnvironmentVariableScope.Set(variables.Select(item => (item.Key, item.Value)).ToArray());
    }

    private static string ProductionPath(string directoryName)
    {
        string root = Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? Path.DirectorySeparatorChar.ToString();

        return Path.Combine(root, "var", "lib", "infra-gate-tests", directoryName);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previousValues;

        private EnvironmentVariableScope(Dictionary<string, string?> previousValues)
        {
            this.previousValues = previousValues;
        }

        public static EnvironmentVariableScope Set(params (string Name, string? Value)[] variables)
        {
            var previousValues = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var variable in variables)
            {
                previousValues[variable.Name] = Environment.GetEnvironmentVariable(variable.Name);
                Environment.SetEnvironmentVariable(variable.Name, variable.Value);
            }

            return new EnvironmentVariableScope(previousValues);
        }

        public void Dispose()
        {
            foreach (var previousValue in previousValues)
            {
                Environment.SetEnvironmentVariable(previousValue.Key, previousValue.Value);
            }
        }
    }
}
