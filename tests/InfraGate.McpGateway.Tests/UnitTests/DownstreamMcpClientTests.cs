using InfraGate.Approvals;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class DownstreamMcpClientTests
{
    [Fact]
    public void CreateTransportOptions_OnlyForwardsAllowedEnvironmentVariables()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.NotNull(transportOptions.EnvironmentVariables);
        Assert.All(transportOptions.EnvironmentVariables, kv =>
        {
            Assert.False(string.IsNullOrEmpty(kv.Key));
            Assert.NotNull(kv.Value);
            Assert.Contains(kv.Key, McpGatewayConventions.DownstreamProcess.AllowedEnvironmentVariables);
        });
    }

    [Fact]
    public void CreateTransportOptions_ExcludesGatewayClientSecret()
    {
        string secretKey = DownstreamAuthConventions.EnvironmentVariables.GatewayClientSecret;
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);
        Environment.SetEnvironmentVariable(secretKey, "super-secret-value");
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.DoesNotContain(secretKey, transportOptions.EnvironmentVariables!.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretKey, null);
        }
    }

    [Fact]
    public void CreateTransportOptions_ExcludesGatewayClientId()
    {
        string clientIdKey = DownstreamAuthConventions.EnvironmentVariables.GatewayClientId;
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);
        Environment.SetEnvironmentVariable(clientIdKey, "infra-gate-gateway");
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.DoesNotContain(clientIdKey, transportOptions.EnvironmentVariables!.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(clientIdKey, null);
        }
    }

    [Fact]
    public void CreateTransportOptions_ExcludesGatewayOAuthAuthority()
    {
        string key = GatewayAuthConventions.EnvironmentVariables.OAuthAuthority;
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);
        Environment.SetEnvironmentVariable(key, "http://keycloak/realms/infra-gate");
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.DoesNotContain(key, transportOptions.EnvironmentVariables!.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Theory]
    [InlineData(RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, "Development")]
    [InlineData(RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment, "Production")]
    [InlineData(RuntimeSafetyConventions.EnvironmentVariables.AspNetCoreEnvironment, "Staging")]
    public void CreateTransportOptions_PassesThroughAllowedVar_WhenSet(string envVarName, string envVarValue)
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);
        string? original = Environment.GetEnvironmentVariable(envVarName);
        Environment.SetEnvironmentVariable(envVarName, envVarValue);
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.Contains(envVarName, transportOptions.EnvironmentVariables!.Keys);
            Assert.Equal(envVarValue, transportOptions.EnvironmentVariables![envVarName]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, original);
        }
    }

    [Theory]
    [InlineData(ApprovalConventions.EnvironmentVariables.ApprovalRoot, "/mnt/approvals")]
    [InlineData(DownstreamAuthConventions.EnvironmentVariables.Required, "true")]
    [InlineData(DownstreamAuthConventions.EnvironmentVariables.Authority, "http://keycloak/realms/infra-gate")]
    [InlineData(DownstreamAuthConventions.EnvironmentVariables.Audience, "urn:infra-gate:mcp-server")]
    [InlineData(DownstreamAuthConventions.EnvironmentVariables.Scope, "mcp:downstream")]
    public void CreateTransportOptions_PassesThroughServerConfigVar_WhenSet(string envVarName, string envVarValue)
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, NullLogger<DownstreamMcpClient>.Instance);
        string? original = Environment.GetEnvironmentVariable(envVarName);
        Environment.SetEnvironmentVariable(envVarName, envVarValue);
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.Contains(envVarName, transportOptions.EnvironmentVariables!.Keys);
            Assert.Equal(envVarValue, transportOptions.EnvironmentVariables![envVarName]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, original);
        }
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
