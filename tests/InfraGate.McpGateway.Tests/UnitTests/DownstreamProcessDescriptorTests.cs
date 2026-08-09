using InfraGate.DownstreamAuth;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.DownstreamAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class DownstreamProcessDescriptorTests
{
    private const string KubernetesMcpServerCommand = ".tools/bin/kubernetes-mcp-server";
    private const string TomlConfigPath = "deploy/generated/local-source-gateway.kubernetes-mcp-server.toml";

    [Fact]
    public void ForKubernetesMcpServer_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DownstreamProcessDescriptor.ForKubernetesMcpServer(null!));
    }

    [Fact]
    public void ForKubernetesMcpServer_PointsAtGoBinaryNotDotnet()
    {
        var options = new KubernetesMcpServerProcessOptions(
            KubernetesMcpServerCommand,
            ["--config", TomlConfigPath],
            "/repo");

        DownstreamProcessDescriptor descriptor = DownstreamProcessDescriptor.ForKubernetesMcpServer(options);

        Assert.Equal(KubernetesMcpServerCommand, descriptor.Command);
        Assert.NotEqual(McpGatewayConventions.DownstreamProcess.Command, descriptor.Command);
    }

    [Fact]
    public void ForKubernetesMcpServer_IncludesConfigFlagAndTomlPathArgument()
    {
        var options = new KubernetesMcpServerProcessOptions(
            KubernetesMcpServerCommand,
            ["--config", TomlConfigPath],
            "/repo");

        DownstreamProcessDescriptor descriptor = DownstreamProcessDescriptor.ForKubernetesMcpServer(options);

        Assert.Equal(["--config", TomlConfigPath], descriptor.Arguments);
    }

    [Fact]
    public void ForKubernetesMcpServer_UsesConfiguredWorkingDirectory()
    {
        var options = new KubernetesMcpServerProcessOptions(KubernetesMcpServerCommand, [], "/repo");

        DownstreamProcessDescriptor descriptor = DownstreamProcessDescriptor.ForKubernetesMcpServer(options);

        Assert.Equal("/repo", descriptor.WorkingDirectory);
    }

    [Fact]
    public void ForKubernetesMcpServer_AuthNeverRequired_NoBootstrapLineIsEverSent()
    {
        var options = new KubernetesMcpServerProcessOptions(KubernetesMcpServerCommand, [], "/repo");

        DownstreamProcessDescriptor descriptor = DownstreamProcessDescriptor.ForKubernetesMcpServer(options);

        // AuthRequired is the exact field DownstreamMcpClient.IsDownstreamAuthRequired() reads to
        // decide whether to send an InfraGate-private bootstrap-auth line before `initialize`.
        // false here means the Go binary is treated purely as a stock, unauthenticated MCP stdio
        // process — no InfraGate protocol is ever spoken to it.
        Assert.False(descriptor.AuthRequired);
    }

    [Fact]
    public void ForKubernetesMcpServer_UsesSecondaryDownstreamName()
    {
        var options = new KubernetesMcpServerProcessOptions(KubernetesMcpServerCommand, [], "/repo");

        DownstreamProcessDescriptor descriptor = DownstreamProcessDescriptor.ForKubernetesMcpServer(options);

        Assert.Equal(McpGatewayConventions.SecondaryDownstream.Name, descriptor.Name);
    }

    [Fact]
    public void CreateTransportOptions_ForKubernetesMcpServer_ProducesGoBinaryCommandAndTomlConfigArgument()
    {
        var options = new KubernetesMcpServerProcessOptions(
            KubernetesMcpServerCommand,
            ["--config", TomlConfigPath],
            "/repo");
        DownstreamProcessDescriptor descriptor = DownstreamProcessDescriptor.ForKubernetesMcpServer(options);
        var client = new DownstreamMcpClient(
            descriptor,
            new NullDownstreamServiceTokenProvider(),
            NullLogger<DownstreamMcpClient>.Instance,
            NullLoggerFactory.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.Equal(KubernetesMcpServerCommand, transportOptions.Command);
        Assert.Equal(["--config", TomlConfigPath], transportOptions.Arguments);
        Assert.Equal("/repo", transportOptions.WorkingDirectory);
        Assert.Equal(McpGatewayConventions.SecondaryDownstream.Name, transportOptions.Name);
    }

    [Fact]
    public void CreateTransportOptions_ForKubernetesMcpServer_NeverForwardsInfraGateEnvironmentVariables()
    {
        var options = new KubernetesMcpServerProcessOptions(KubernetesMcpServerCommand, [], "/repo");
        DownstreamProcessDescriptor descriptor = DownstreamProcessDescriptor.ForKubernetesMcpServer(options);
        var client = new DownstreamMcpClient(
            descriptor,
            new NullDownstreamServiceTokenProvider(),
            NullLogger<DownstreamMcpClient>.Instance,
            NullLoggerFactory.Instance);

        string key = DownstreamAuthConventions.EnvironmentVariables.GatewayClientSecret;
        Environment.SetEnvironmentVariable(key, "super-secret-value");
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.DoesNotContain(key, transportOptions.EnvironmentVariables!.Keys);
            Assert.All(
                transportOptions.EnvironmentVariables!.Keys,
                envVarName => Assert.DoesNotContain("InfraGate", envVarName, StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void ForPrimary_ReflectsDownstreamAuthRequired()
    {
        var auth = new GatewayAuthOptions("https://issuer.example.com");
        var downstreamAuth = new DownstreamAuthOptions
        {
            Required = true,
            Authority = "https://idp.example.com",
            GatewayClientId = "infra-gate-gateway",
            GatewayClientSecret = "secret",
        };
        var options = new McpGatewayOptions(
            auth,
            DownstreamProject: "server.csproj",
            GuardAuditRoot: "guardrails",
            WorkingDirectory: "/repo",
            ApprovalRoot: "approvals",
            ApprovalBaseUrl: null,
            ApprovalChallengeTtl: McpGatewayOptions.DefaultApprovalChallengeTtl,
            DownstreamAuth: downstreamAuth);

        DownstreamProcessDescriptor descriptor = DownstreamProcessDescriptor.ForPrimary(options);

        Assert.True(descriptor.AuthRequired);
        Assert.Equal(McpGatewayConventions.DownstreamProcess.Command, descriptor.Command);
    }
}
