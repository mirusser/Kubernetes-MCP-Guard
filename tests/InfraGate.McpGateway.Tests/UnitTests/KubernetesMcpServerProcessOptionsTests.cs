using InfraGate.DownstreamAuth;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.BinaryIntegrity;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class KubernetesMcpServerProcessOptionsTests
{
    private const string Command = ".tools/bin/kubernetes-mcp-server";
    private const string OAuthAuthority = "https://issuer.example.com";

    [Fact]
    public void FromConfiguration_NullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => KubernetesMcpServerProcessOptions.FromConfiguration(null!));
    }

    [Fact]
    public void FromConfiguration_WithoutCommandConfigured_ReturnsNull()
    {
        var configuration = BuildConfig();

        KubernetesMcpServerProcessOptions? options = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.Null(options);
    }

    [Fact]
    public void FromConfiguration_WhitespaceOnlyCommand_ReturnsNull()
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", "   "));

        KubernetesMcpServerProcessOptions? options = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.Null(options);
    }

    [Fact]
    public void FromConfiguration_MalformedArgumentsShape_FallsBackToEmptyArray()
    {
        // "Arguments" configured as a scalar value rather than an indexed array
        // (Arguments:0, Arguments:1, ...) has no bindable children, so Get<string[]>()
        // returns null and the production code's `?? []` fallback applies.
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments", "--config deploy/generated/k8s-mcp.toml"));

        KubernetesMcpServerProcessOptions? options = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.NotNull(options);
        Assert.Empty(options.Arguments);
    }

    [Fact]
    public void FromConfiguration_WithCommandAndArgumentsConfigured_ReturnsPopulatedOptions()
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:0", "--config"),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:1", "deploy/generated/k8s-mcp.toml"),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":WorkingDirectory", "/repo"));

        KubernetesMcpServerProcessOptions? options = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.NotNull(options);
        Assert.Equal(Command, options.Command);
        Assert.Equal(["--config", "deploy/generated/k8s-mcp.toml"], options.Arguments);
        Assert.Equal("/repo", options.WorkingDirectory);
    }

    [Fact]
    public void FromConfiguration_WithoutWorkingDirectoryConfigured_DefaultsToCurrentDirectory()
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command));

        KubernetesMcpServerProcessOptions? options = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.NotNull(options);
        Assert.Equal(Directory.GetCurrentDirectory(), options.WorkingDirectory);
    }

    [Fact]
    public void AuthRequired_IsAlwaysFalse()
    {
        Assert.False(KubernetesMcpServerProcessOptions.AuthRequired);
    }

    [Fact]
    public void ValidateProductionSafety_UnaffectedByKubernetesMcpServerOptionsPresence()
    {
        var configuration = BuildProductionConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        KubernetesMcpServerProcessOptions? secondary = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.NotNull(secondary);
        Exception? exception = Record.Exception(() => options.ValidateProductionSafety(
            new FakeVerifier(options.DownstreamAssembly, options.DownstreamAssemblyHash)));

        Assert.Null(exception);
    }

    private sealed class FakeVerifier(string? assembly, string? hash) : IDownstreamBinaryIntegrityVerifier
    {
        public void Verify(string downstreamAssembly, string expectedHash)
        {
            Assert.Equal(assembly, downstreamAssembly);
            Assert.Equal(hash, expectedHash);
        }
    }

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
            [GatewayAuthConventions.ConfigurationKeys.OAuthResource] = "https://gateway.example.com/mcp",
            [GatewayAuthConventions.ConfigurationKeys.OAuthScope] = GatewayAuthConventions.DefaultOAuthScope,
            [GatewayAuthConventions.ConfigurationKeys.OAuthRequireHttpsMetadata] = "true",
            [GatewayAuthConventions.ConfigurationKeys.ApprovalOAuthAuthorizationEndpoint] = OAuthAuthority + "/authorize",
            [GatewayAuthConventions.ConfigurationKeys.ApprovalOAuthTokenEndpoint] = OAuthAuthority + "/token",
            [GatewayAuthConventions.ConfigurationKeys.TokenIntrospectionEnabled] = "true",
            [GatewayAuthConventions.ConfigurationKeys.TokenIntrospectionClientId] = "gateway-resource-server",
            [GatewayAuthConventions.ConfigurationKeys.TokenIntrospectionClientSecret] = "secret-placeholder",
            [GatewayAuthConventions.ConfigurationKeys.MaxAcceptedAccessTokenLifetimeSeconds] = "300",
            [McpGatewayConventions.ConfigurationKeys.ApprovalBaseUrl] = "https://gateway.example.com",
            [McpGatewayConventions.ConfigurationKeys.GuardAuditRoot] = "/var/lib/infra-gate/production/guardrails",
            [McpGatewayConventions.ConfigurationKeys.ApprovalRoot] = "/var/lib/infra-gate/production/approvals",
            [McpGatewayConventions.ConfigurationKeys.DownstreamAssembly] = "/app/server/InfraGate.McpServer.dll",
            [McpGatewayConventions.ConfigurationKeys.DownstreamAssemblyHash] =
                "a3e5f8c9d2b1e4076f5a3c8e1d0b9a2c7f4e6d5b8c3a1f0e9d7b6c5a4f3e2d1b0",
            [DownstreamAuthConventions.ConfigurationKeys.Required] = "true",
            [DownstreamAuthConventions.ConfigurationKeys.Authority] = "https://idp.example.com",
            [DownstreamAuthConventions.ConfigurationKeys.RequireHttpsMetadata] = "true",
            [DownstreamAuthConventions.ConfigurationKeys.GatewayClientId] = "infra-gate-gateway",
        };

        foreach ((string key, string? value) in overrides)
        {
            entries[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(entries).Build();
    }
}
