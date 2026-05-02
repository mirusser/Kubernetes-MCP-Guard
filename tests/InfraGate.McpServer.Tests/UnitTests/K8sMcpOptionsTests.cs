using InfraGate.McpServer;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sMcpOptionsTests
{
    private const string ApprovalRootVariable = "K8S_MCP_APPROVAL_ROOT";
    private const string AllowedNamespacesVariable = "K8S_MCP_ALLOWED_NAMESPACES";
    private const string DefaultApprovalRootDirectory = ".mcp-approvals";

    [Fact]
    public void FromEnvironment_UsesDefaultApprovalRootAndNamespace_WhenUnset()
    {
        using var environment = EnvironmentVariableScope.Set(
            (ApprovalRootVariable, null),
            (AllowedNamespacesVariable, null));

        var options = K8sMcpOptions.FromEnvironment();

        Assert.Equal(
            Path.Combine(Directory.GetCurrentDirectory(), DefaultApprovalRootDirectory),
            options.ApprovalRoot);
        Assert.Equal([K8sMcpOptions.DefaultNamespace], options.AllowedNamespaces);
    }

    [Fact]
    public void FromEnvironment_UsesConfiguredApprovalRoot_WhenSet()
    {
        using var environment = EnvironmentVariableScope.Set(
            (ApprovalRootVariable, "/tmp/infra-gate-approvals"),
            (AllowedNamespacesVariable, null));

        var options = K8sMcpOptions.FromEnvironment();

        Assert.Equal("/tmp/infra-gate-approvals", options.ApprovalRoot);
    }

    [Fact]
    public void FromEnvironment_UsesConfiguredNamespaces_WhenSet()
    {
        using var environment = EnvironmentVariableScope.Set(
            (ApprovalRootVariable, null),
            (AllowedNamespacesVariable, "alpha, beta ,,gamma"));

        var options = K8sMcpOptions.FromEnvironment();

        Assert.Equal(["alpha", "beta", "gamma"], options.AllowedNamespaces.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ParseAllowedNamespaces_UsesDefault_WhenUnset()
    {
        var namespaces = K8sMcpOptions.ParseAllowedNamespaces(null);

        Assert.Contains(K8sMcpOptions.DefaultNamespace, namespaces);
        Assert.Single(namespaces);
    }

    [Fact]
    public void ParseAllowedNamespaces_TrimsCommaSeparatedValues()
    {
        var namespaces = K8sMcpOptions.ParseAllowedNamespaces("alpha, beta ,,gamma");

        Assert.Equal(["alpha", "beta", "gamma"], namespaces.Order(StringComparer.Ordinal));
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
