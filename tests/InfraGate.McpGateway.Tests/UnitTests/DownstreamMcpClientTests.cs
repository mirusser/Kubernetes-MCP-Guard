using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class DownstreamMcpClientTests
{
    [Fact]
    public void CreateTransportOptions_ForwardsAllEnvironmentVariables()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.NotNull(transportOptions.EnvironmentVariables);
        Assert.NotEmpty(transportOptions.EnvironmentVariables);
        Assert.All(transportOptions.EnvironmentVariables, kv =>
        {
            Assert.False(string.IsNullOrEmpty(kv.Key));
            Assert.NotNull(kv.Value);
        });
    }

    [Fact]
    public void CreateTransportOptions_UsesAssemblyArguments_WhenDownstreamAssemblySet()
    {
        string downstreamProject = "/app/server/InfraGate.McpServer.dll";

        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory(), downstreamAssembly: "/app/server/InfraGate.McpServer.dll");
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.NotNull(transportOptions.Arguments);
        string arguments = Assert.Single(transportOptions.Arguments!);
        Assert.Equal("/app/server/InfraGate.McpServer.dll", arguments);
    }

    [Fact]
    public void CreateTransportOptions_UsesRunProjectArguments_WhenDownstreamAssemblyNotSet()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.NotNull(transportOptions.Arguments);
        int argCount = transportOptions.Arguments!.Count;
        Assert.Equal(3, argCount);
        Assert.Equal(McpGatewayConventions.DownstreamProcess.RunArgument, transportOptions.Arguments![0]);
        Assert.Equal(McpGatewayConventions.DownstreamProcess.ProjectArgument, transportOptions.Arguments![1]);
        Assert.Equal(downstreamProject, transportOptions.Arguments![2]);
    }

    [Fact]
    public void CreateTransportOptions_UsesRunProjectArguments_WhenDownstreamAssemblyWhitespace()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory(), downstreamAssembly: "   ");
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.NotNull(transportOptions.Arguments);
        Assert.Equal(3, transportOptions.Arguments!.Count);
        Assert.Equal("run", transportOptions.Arguments![0]);
        Assert.Equal("--project", transportOptions.Arguments![1]);
    }

    [Fact]
    public void CreateTransportOptions_SetsWorkingDirectory()
    {
        string workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        try
        {
            string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
            var options = CreateOptions(downstreamProject, workingDirectory: workingDirectory);
            var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);

            var transportOptions = client.CreateTransportOptions();

            Assert.Equal(workingDirectory, transportOptions.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateTransportOptions_SetsShutdownTimeout()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.Equal(TimeSpan.FromSeconds(10), transportOptions.ShutdownTimeout);
    }

    [Fact]
    public void CreateTransportOptions_SetsNameAndCommand()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.Equal(McpGatewayConventions.DownstreamProcess.Name, transportOptions.Name);
        Assert.Equal(McpGatewayConventions.DownstreamProcess.Command, transportOptions.Command);
    }

    private static McpGatewayOptions CreateOptions(
        string downstreamProject,
        string workingDirectory,
        string? downstreamAssembly = null)
    {
        var authOptions = new GatewayAuthOptions(
            OAuthAuthority: "http://127.0.0.1:3010/realms/infra-gate",
            OAuthResource: GatewayAuthConventions.DefaultOAuthResource,
            OAuthScope: GatewayAuthConventions.DefaultOAuthScope,
            OAuthRequireHttpsMetadata: false);

        return new McpGatewayOptions(
            authOptions,
            DownstreamProject: downstreamProject,
            GuardAuditRoot: Path.Combine(Path.GetTempPath(), "audit"),
            WorkingDirectory: workingDirectory,
            ApprovalRoot: Path.Combine(Path.GetTempPath(), "approvals"),
            ApprovalBaseUrl: null,
            ApprovalChallengeTtl: McpGatewayOptions.DefaultApprovalChallengeTtl,
            DownstreamAssembly: downstreamAssembly);
    }
}
