using InfraGate.DownstreamAuth;
using InfraGate.McpServer;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesMcpOptionsTests
{
    private const string DownstreamAuthority = "https://idp.example.com";

    [Fact]
    public void Constructor_UsesOptionalDefaults_WhenFlagsOmitted()
    {
        var options = new KubernetesMcpOptions(
            new HashSet<string>(["demo"], StringComparer.Ordinal));

        Assert.True(options.HasExplicitAllowedNamespaces);
        Assert.False(options.HasExplicitKubeConfig);
        Assert.False(options.IsInClusterConfigEnabled);
    }

    [Fact]
    public void FromEnvironment_UsesDefaultNamespace_WhenUnset()
    {
        using var environment = EnvironmentVariableScope.Set(
            (KubernetesConventions.EnvironmentVariables.AllowedNamespaces, null));

        var options = KubernetesMcpOptions.FromEnvironment();

        Assert.Equal([KubernetesMcpOptions.DefaultNamespace], options.AllowedNamespaces);
    }

    [Fact]
    public void FromEnvironment_UsesConfiguredNamespaces_WhenSet()
    {
        using var environment = EnvironmentVariableScope.Set(
            (KubernetesConventions.EnvironmentVariables.AllowedNamespaces, "alpha, beta ,,gamma"));

        var options = KubernetesMcpOptions.FromEnvironment();

        Assert.Equal(["alpha", "beta", "gamma"], options.AllowedNamespaces.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FromEnvironment_ParsesInClusterConfigFlag_WhenSet()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (KubernetesConventions.EnvironmentVariables.AllowedNamespaces, null),
            (KubernetesConventions.EnvironmentVariables.KubeConfig, null),
            (KubernetesConventions.EnvironmentVariables.UseInClusterConfig, "true"));

        var options = KubernetesMcpOptions.FromEnvironment();

        Assert.True(options.IsInClusterConfigEnabled);
        Assert.False(options.HasExplicitKubeConfig);
    }

    [Fact]
    public void FromEnvironment_WithInvalidInClusterConfigFlag_Throws()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (KubernetesConventions.EnvironmentVariables.UseInClusterConfig, "maybe"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(KubernetesMcpOptions.FromEnvironment);

        Assert.Contains(KubernetesConventions.EnvironmentVariables.UseInClusterConfig, exception.Message);
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
                [KubernetesConventions.ConfigurationKeys.KubeConfig] = "/var/lib/kubeconfig",
                [KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":0"] = "alpha",
                [KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":1"] = "beta"
            })
            .Build();

        var options = KubernetesMcpOptions.FromConfiguration(configuration);

        Assert.Equal(RuntimeMode.Production, options.RuntimeMode);
        Assert.Equal("/var/lib/kubeconfig", options.KubeConfig);
        Assert.Equal(["alpha", "beta"], options.AllowedNamespaces.Order(StringComparer.Ordinal));
        Assert.True(options.HasExplicitAllowedNamespaces);
    }

    [Fact]
    public void FromConfiguration_BindsFromKubernetesSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [KubernetesConventions.ConfigurationKeys.KubeConfig] = "/json/kubeconfig",
                [KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":0"] = "alpha",
                [KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":1"] = "beta"
            })
            .Build();

        var options = KubernetesMcpOptions.FromConfiguration(configuration);

        Assert.Equal("/json/kubeconfig", options.KubeConfig);
        Assert.Equal(["alpha", "beta"], options.AllowedNamespaces.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ParseAllowedNamespaces_UsesDefault_WhenUnset()
    {
        var namespaces = KubernetesMcpOptions.ParseAllowedNamespaces(null);

        Assert.Contains(KubernetesMcpOptions.DefaultNamespace, namespaces);
        Assert.Single(namespaces);
    }

    [Fact]
    public void ParseAllowedNamespaces_TrimsCommaSeparatedValues()
    {
        var namespaces = KubernetesMcpOptions.ParseAllowedNamespaces("alpha, beta ,,gamma");

        Assert.Equal(["alpha", "beta", "gamma"], namespaces.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ProductionMode_WithDefaultKubeConfigFallback_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (KubernetesConventions.EnvironmentVariables.KubeConfig, null),
            (KubernetesConventions.EnvironmentVariables.UseInClusterConfig, null));

        var options = KubernetesMcpOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(KubernetesConventions.EnvironmentVariables.KubeConfig, exception.Message);
        Assert.Contains(KubernetesConventions.EnvironmentVariables.UseInClusterConfig, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithoutExplicitNamespaces_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (KubernetesConventions.EnvironmentVariables.AllowedNamespaces, null));

        var options = KubernetesMcpOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(KubernetesConventions.EnvironmentVariables.AllowedNamespaces, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithValidProductionSettings_AllowsStartup()
    {
        using var environment = SetProductionEnvironment();

        var options = KubernetesMcpOptions.FromEnvironment();
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void DevelopmentMode_AllowsLocalDefaults()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (KubernetesConventions.EnvironmentVariables.AllowedNamespaces, null),
            (KubernetesConventions.EnvironmentVariables.KubeConfig, null),
            (KubernetesConventions.EnvironmentVariables.UseInClusterConfig, null));

        var options = KubernetesMcpOptions.FromEnvironment();
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void ProductionMode_WithDownstreamAuthRequired_False_RefusesStartup()
    {
        using var environment = SetProductionEnvironment(
            (DownstreamAuthConventions.EnvironmentVariables.Required, "false"));

        var options = KubernetesMcpOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.Required, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithValidDownstreamAuth_AllowsStartup()
    {
        using var environment = SetProductionEnvironment();

        var options = KubernetesMcpOptions.FromEnvironment();
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void DevelopmentMode_WithDownstreamAuthRequired_False_AllowsStartup()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (DownstreamAuthConventions.EnvironmentVariables.Required, "false"),
            (KubernetesConventions.EnvironmentVariables.AllowedNamespaces, null),
            (KubernetesConventions.EnvironmentVariables.KubeConfig, null),
            (KubernetesConventions.EnvironmentVariables.UseInClusterConfig, null));

        var options = KubernetesMcpOptions.FromEnvironment();
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void FromEnvironment_WithDownstreamAuthRequired_Absent_DefaultsToRequired()
    {
        using var environment = EnvironmentVariableScope.Set(
            (DownstreamAuthConventions.EnvironmentVariables.Required, null));

        var options = KubernetesMcpOptions.FromEnvironment();

        Assert.True(options.DownstreamAuth?.Required);
    }

    [Fact]
    public void FromEnvironment_PopulatesDownstreamAuth_FromEnvironment()
    {
        using var environment = EnvironmentVariableScope.Set(
            (DownstreamAuthConventions.EnvironmentVariables.Required, "true"),
            (DownstreamAuthConventions.EnvironmentVariables.Authority, DownstreamAuthority));

        var options = KubernetesMcpOptions.FromEnvironment();

        Assert.NotNull(options.DownstreamAuth);
        Assert.True(options.DownstreamAuth.Required);
        Assert.Equal(DownstreamAuthority, options.DownstreamAuth.Authority);
    }

    private static EnvironmentVariableScope SetProductionEnvironment(params (string Name, string? Value)[] overrides)
    {
        string kubeConfig = Path.Combine(
            Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? Path.DirectorySeparatorChar.ToString(),
            "var", "lib", "infra-gate-tests", "kubeconfig");

        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment] =
                RuntimeSafetyConventions.EnvironmentValues.Production,
            [KubernetesConventions.EnvironmentVariables.AllowedNamespaces] = "mcp-nginx-demo",
            [KubernetesConventions.EnvironmentVariables.KubeConfig] = kubeConfig,
            [KubernetesConventions.EnvironmentVariables.UseInClusterConfig] = null,
            [DownstreamAuthConventions.EnvironmentVariables.Required] = "true",
            [DownstreamAuthConventions.EnvironmentVariables.Authority] = DownstreamAuthority
        };

        foreach (var item in overrides)
        {
            variables[item.Name] = item.Value;
        }

        return EnvironmentVariableScope.Set(variables.Select(item => (item.Key, item.Value)).ToArray());
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
