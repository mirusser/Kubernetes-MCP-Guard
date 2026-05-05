using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class McpGatewayOptionsTests
{
    private const string OAuthAuthority = "https://issuer.example.com";
    private const string DownstreamAssembly = "/app/server/InfraGate.McpServer.dll";
    private const string DownstreamProject = "server.csproj";
    private const string GuardAuditRoot = "guardrails";
    private const string WorkingDirectory = "/repo";
    private const string ApprovalRoot = "approvals";

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
    public void CreateTransportOptions_UsesProjectRunArguments_WhenAssemblyUnset()
    {
        var client = new DownstreamMcpClient(CreateOptions());

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
        var client = new DownstreamMcpClient(CreateOptions(DownstreamAssembly));

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
        var client = new DownstreamMcpClient(CreateOptions(assembly));

        var transportOptions = client.CreateTransportOptions();

        Assert.Equal(
            [
                McpGatewayConventions.DownstreamProcess.RunArgument,
                McpGatewayConventions.DownstreamProcess.ProjectArgument,
                DownstreamProject
            ],
            transportOptions.Arguments);
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
