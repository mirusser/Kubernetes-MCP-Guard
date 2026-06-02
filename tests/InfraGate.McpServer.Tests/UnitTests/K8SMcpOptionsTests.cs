using InfraGate.DownstreamAuth;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesMcpOptionsTests
{
    private const string DownstreamAuthority = "https://idp.example.com";

    private static IConfiguration BuildConfig(params (string Key, string? Value)[] entries)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();
    }

    private static IConfiguration BuildProductionConfig(params (string Key, string? Value)[] overrides)
    {
        string kubeConfig = Path.Combine(
            Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? Path.DirectorySeparatorChar.ToString(),
            "var", "lib", "infra-gate-tests", "kubeconfig");

        var entries = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment] =
                RuntimeSafetyConventions.EnvironmentValues.Production,
            [KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":0"] = "mcp-nginx-demo",
            [KubernetesConventions.ConfigurationKeys.KubeConfig] = kubeConfig,
            [DownstreamAuthConventions.ConfigurationKeys.Required] = "true",
            [DownstreamAuthConventions.ConfigurationKeys.Authority] = DownstreamAuthority
        };

        foreach (var item in overrides)
        {
            if (item.Value is null)
            {
                entries.Remove(item.Key);
            }
            else
            {
                entries[item.Key] = item.Value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(entries).Build();
    }

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
    public void FromConfiguration_UsesDefaultNamespace_WhenSectionAbsent()
    {
        var config = BuildConfig();

        var options = KubernetesMcpOptions.FromConfiguration(config);

        Assert.Equal([KubernetesMcpOptions.DefaultNamespace], options.AllowedNamespaces);
        Assert.False(options.HasExplicitAllowedNamespaces);
    }

    [Fact]
    public void FromConfiguration_ParsesAllowedNamespaces_FromSection()
    {
        var config = BuildConfig(
            (KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":0", "alpha"),
            (KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":1", "beta"),
            (KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":2", "gamma"));

        var options = KubernetesMcpOptions.FromConfiguration(config);

        Assert.Equal(["alpha", "beta", "gamma"], options.AllowedNamespaces.Order(StringComparer.Ordinal));
        Assert.True(options.HasExplicitAllowedNamespaces);
    }

    [Fact]
    public void FromConfiguration_BindsUseInClusterConfig()
    {
        var config = BuildConfig(
            (RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (KubernetesConventions.ConfigurationKeys.UseInClusterConfig, "true"));

        var options = KubernetesMcpOptions.FromConfiguration(config);

        Assert.True(options.IsInClusterConfigEnabled);
        Assert.False(options.HasExplicitKubeConfig);
    }

    [Fact]
    public void FromConfiguration_UsesGeneratedAppSettingsValues()
    {
        var configuration = BuildConfig(
            (RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment, RuntimeSafetyConventions.EnvironmentValues.Production),
            (KubernetesConventions.ConfigurationKeys.KubeConfig, "/var/lib/kubeconfig"),
            (KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":0", "alpha"),
            (KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":1", "beta"));

        var options = KubernetesMcpOptions.FromConfiguration(configuration);

        Assert.Equal(RuntimeMode.Production, options.RuntimeMode);
        Assert.Equal("/var/lib/kubeconfig", options.KubeConfig);
        Assert.Equal(["alpha", "beta"], options.AllowedNamespaces.Order(StringComparer.Ordinal));
        Assert.True(options.HasExplicitAllowedNamespaces);
    }

    [Fact]
    public void FromConfiguration_BindsFromKubernetesSection()
    {
        var configuration = BuildConfig(
            (KubernetesConventions.ConfigurationKeys.KubeConfig, "/json/kubeconfig"),
            (KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":0", "alpha"),
            (KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":1", "beta"));

        var options = KubernetesMcpOptions.FromConfiguration(configuration);

        Assert.Equal("/json/kubeconfig", options.KubeConfig);
        Assert.Equal(["alpha", "beta"], options.AllowedNamespaces.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FromConfiguration_BindsDownstreamAuthSection()
    {
        var config = BuildConfig(
            (DownstreamAuthConventions.ConfigurationKeys.Required, "true"),
            (DownstreamAuthConventions.ConfigurationKeys.Authority, DownstreamAuthority));

        var options = KubernetesMcpOptions.FromConfiguration(config);

        Assert.NotNull(options.DownstreamAuth);
        Assert.True(options.DownstreamAuth!.Required);
        Assert.Equal(DownstreamAuthority, options.DownstreamAuth.Authority);
    }

    [Fact]
    public void FromConfiguration_AbsentDownstreamAuthSection_LeavesDownstreamAuthNull()
    {
        var config = BuildConfig(
            (KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":0", "demo"));

        var options = KubernetesMcpOptions.FromConfiguration(config);

        Assert.Null(options.DownstreamAuth);
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
        var config = BuildProductionConfig(
            (KubernetesConventions.ConfigurationKeys.KubeConfig, null));

        var options = KubernetesMcpOptions.FromConfiguration(config);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(KubernetesConventions.EnvironmentVariables.KubeConfig, exception.Message);
        Assert.Contains(KubernetesConventions.EnvironmentVariables.UseInClusterConfig, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithoutExplicitNamespaces_RefusesStartup()
    {
        var config = BuildProductionConfig(
            (KubernetesConventions.ConfigurationKeys.AllowedNamespaces + ":0", null));

        var options = KubernetesMcpOptions.FromConfiguration(config);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(KubernetesConventions.EnvironmentVariables.AllowedNamespaces, exception.Message);
    }

    [Fact]
    public void ValidateProductionSafety_WithValidProductionSettings_AllowsStartup()
    {
        var config = BuildProductionConfig();

        var options = KubernetesMcpOptions.FromConfiguration(config);
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void DevelopmentMode_AllowsLocalDefaults()
    {
        var config = BuildConfig(
            (RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development));

        var options = KubernetesMcpOptions.FromConfiguration(config);
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void ProductionMode_WithDownstreamAuthRequired_False_RefusesStartup()
    {
        var config = BuildProductionConfig(
            (DownstreamAuthConventions.ConfigurationKeys.Required, "false"));

        var options = KubernetesMcpOptions.FromConfiguration(config);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.Required, exception.Message);
    }

    [Fact]
    public void ProductionMode_WithValidDownstreamAuth_AllowsStartup()
    {
        var config = BuildProductionConfig();

        var options = KubernetesMcpOptions.FromConfiguration(config);
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void DevelopmentMode_WithDownstreamAuthRequired_False_AllowsStartup()
    {
        var config = BuildConfig(
            (RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (DownstreamAuthConventions.ConfigurationKeys.Required, "false"));

        var options = KubernetesMcpOptions.FromConfiguration(config);
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
    }

    [Fact]
    public void ProductionMode_BothKubeConfigAndInCluster_RefusesStartup()
    {
        string kubeConfig = Path.Combine(
            Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? Path.DirectorySeparatorChar.ToString(),
            "var", "lib", "infra-gate-tests", "kubeconfig");

        var config = BuildConfig(
            (RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (KubernetesConventions.ConfigurationKeys.KubeConfig, kubeConfig),
            (KubernetesConventions.ConfigurationKeys.UseInClusterConfig, "true"));

        var options = KubernetesMcpOptions.FromConfiguration(config);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains(KubernetesConventions.EnvironmentVariables.KubeConfig, exception.Message);
        Assert.Contains(KubernetesConventions.EnvironmentVariables.UseInClusterConfig, exception.Message);
    }
}
