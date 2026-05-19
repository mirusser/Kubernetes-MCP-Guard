using InfraGate.McpServer;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8SMcpOptionsTests
{
    private const string DefaultApprovalRootDirectory = ".mcp-approvals";

    [Fact]
    public void Constructor_UsesOptionalDefaults_WhenFlagsOmitted()
    {
        var options = new K8SMcpOptions(
            new HashSet<string>(["demo"], StringComparer.Ordinal),
            ProductionPath("approvals"));

        Assert.True(options.IsApprovalRootExplicit);
        Assert.True(options.HasExplicitAllowedNamespaces);
        Assert.False(options.HasExplicitKubeConfig);
        Assert.False(options.IsInClusterConfigEnabled);
    }

    [Fact]
    public void FromEnvironment_UsesDefaultApprovalRootAndNamespace_WhenUnset()
    {
        using var environment = EnvironmentVariableScope.Set(
            (K8sConventions.EnvironmentVariables.ApprovalRoot, null),
            (K8sConventions.EnvironmentVariables.AllowedNamespaces, null));

        var options = K8SMcpOptions.FromEnvironment();

        Assert.Equal(
            Path.Combine(Directory.GetCurrentDirectory(), DefaultApprovalRootDirectory),
            options.ApprovalRoot);
        Assert.Equal([K8SMcpOptions.DefaultNamespace], options.AllowedNamespaces);
    }

    [Fact]
    public void FromEnvironment_UsesConfiguredApprovalRoot_WhenSet()
    {
        using var environment = EnvironmentVariableScope.Set(
            (K8sConventions.EnvironmentVariables.ApprovalRoot, "/tmp/infra-gate-approvals"),
            (K8sConventions.EnvironmentVariables.AllowedNamespaces, null));

        var options = K8SMcpOptions.FromEnvironment();

        Assert.Equal("/tmp/infra-gate-approvals", options.ApprovalRoot);
    }

    [Fact]
    public void FromEnvironment_UsesConfiguredNamespaces_WhenSet()
    {
        using var environment = EnvironmentVariableScope.Set(
            (K8sConventions.EnvironmentVariables.ApprovalRoot, null),
            (K8sConventions.EnvironmentVariables.AllowedNamespaces, "alpha, beta ,,gamma"));

        var options = K8SMcpOptions.FromEnvironment();

        Assert.Equal(["alpha", "beta", "gamma"], options.AllowedNamespaces.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FromEnvironment_ParsesInClusterConfigFlag_WhenSet()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (K8sConventions.EnvironmentVariables.ApprovalRoot, null),
            (K8sConventions.EnvironmentVariables.AllowedNamespaces, null),
            (K8sConventions.EnvironmentVariables.KubeConfig, null),
            (K8sConventions.EnvironmentVariables.UseInClusterConfig, "true"));

        var options = K8SMcpOptions.FromEnvironment();

        Assert.True(options.IsInClusterConfigEnabled);
        Assert.False(options.HasExplicitKubeConfig);
    }

    [Fact]
    public void FromEnvironment_WithInvalidInClusterConfigFlag_Throws()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (K8sConventions.EnvironmentVariables.UseInClusterConfig, "maybe"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(K8SMcpOptions.FromEnvironment);

        Assert.Contains(K8sConventions.EnvironmentVariables.UseInClusterConfig, exception.Message);
        Assert.Contains("maybe", exception.Message);
    }

    [Fact]
    public void FromConfiguration_UsesGeneratedAppSettingsValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment] =
                    RuntimeSafetyConventions.EnvironmentValues.Production,
                [K8sConventions.ConfigurationKeys.ApprovalRoot] = ProductionPath("approvals"),
                [K8sConventions.ConfigurationKeys.KubeConfig] = ProductionPath("kubeconfig"),
                [K8sConventions.ConfigurationKeys.AllowedNamespaces + ":0"] = "alpha",
                [K8sConventions.ConfigurationKeys.AllowedNamespaces + ":1"] = "beta"
            })
            .Build();

        var options = K8SMcpOptions.FromConfiguration(configuration);

        Assert.Equal(RuntimeMode.Production, options.RuntimeMode);
        Assert.Equal(ProductionPath("approvals"), options.ApprovalRoot);
        Assert.Equal(ProductionPath("kubeconfig"), options.KubeConfig);
        Assert.Equal(["alpha", "beta"], options.AllowedNamespaces.Order(StringComparer.Ordinal));
        Assert.True(options.IsApprovalRootExplicit);
        Assert.True(options.HasExplicitAllowedNamespaces);
    }

    [Fact]
    public void FromConfiguration_BindsFromKubernetesSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [K8sConventions.ConfigurationKeys.KubeConfig] = "/json/kubeconfig",
                [K8sConventions.ConfigurationKeys.ApprovalRoot] = "/data/approvals",
                [K8sConventions.ConfigurationKeys.AllowedNamespaces + ":0"] = "alpha",
                [K8sConventions.ConfigurationKeys.AllowedNamespaces + ":1"] = "beta"
            })
            .Build();

        var options = K8SMcpOptions.FromConfiguration(configuration);

        Assert.Equal("/json/kubeconfig", options.KubeConfig);
        Assert.Equal("/data/approvals", options.ApprovalRoot);
        Assert.Equal(["alpha", "beta"], options.AllowedNamespaces.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ParseAllowedNamespaces_UsesDefault_WhenUnset()
    {
        var namespaces = K8SMcpOptions.ParseAllowedNamespaces(null);

        Assert.Contains(K8SMcpOptions.DefaultNamespace, namespaces);
        Assert.Single(namespaces);
    }

    [Fact]
    public void ParseAllowedNamespaces_TrimsCommaSeparatedValues()
    {
        var namespaces = K8SMcpOptions.ParseAllowedNamespaces("alpha, beta ,,gamma");

        Assert.Equal(["alpha", "beta", "gamma"], namespaces.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ProductionMode_WithDefaultKubeConfigFallback_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (K8sConventions.EnvironmentVariables.KubeConfig, null),
            (K8sConventions.EnvironmentVariables.UseInClusterConfig, null));

        var options = K8SMcpOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(K8sConventions.EnvironmentVariables.KubeConfig, exception.Message);
        Assert.Contains(K8sConventions.EnvironmentVariables.UseInClusterConfig, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithoutExplicitNamespaces_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (K8sConventions.EnvironmentVariables.AllowedNamespaces, null));

        var options = K8SMcpOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(K8sConventions.EnvironmentVariables.AllowedNamespaces, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithValidProductionSettings_AllowsStartup()
    {
        using var environment = SetProductionEnvironment();

        var options = K8SMcpOptions.FromEnvironment();
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void DevelopmentMode_AllowsLocalDefaults()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (K8sConventions.EnvironmentVariables.ApprovalRoot, null),
            (K8sConventions.EnvironmentVariables.AllowedNamespaces, null),
            (K8sConventions.EnvironmentVariables.KubeConfig, null),
            (K8sConventions.EnvironmentVariables.UseInClusterConfig, null));

        var options = K8SMcpOptions.FromEnvironment();
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    private static EnvironmentVariableScope SetProductionEnvironment(params (string Name, string? Value)[] overrides)
    {
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment] =
                RuntimeSafetyConventions.EnvironmentValues.Production,
            [K8sConventions.EnvironmentVariables.ApprovalRoot] = ProductionPath("approvals"),
            [K8sConventions.EnvironmentVariables.AllowedNamespaces] = "mcp-nginx-demo",
            [K8sConventions.EnvironmentVariables.KubeConfig] = ProductionPath("kubeconfig"),
            [K8sConventions.EnvironmentVariables.UseInClusterConfig] = null
        };

        foreach (var item in overrides)
        {
            variables[item.Name] = item.Value;
        }

        return EnvironmentVariableScope.Set(variables.Select(item => (item.Key, item.Value)).ToArray());
    }

    private static string ProductionPath(string fileName)
    {
        string root = Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? Path.DirectorySeparatorChar.ToString();

        return Path.Combine(root, "var", "lib", "infra-gate-tests", fileName);
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
