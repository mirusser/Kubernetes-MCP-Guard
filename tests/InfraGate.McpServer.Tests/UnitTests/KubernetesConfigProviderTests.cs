using InfraGate.McpServer;
using InfraGate.RuntimeSafety;
using k8s;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesConfigProviderTests
{
    [Fact]
    public void Create_UsesKubeConfigFactory_WhenKubeConfigSet()
    {
        string kubeConfig = ProductionPath("kubeconfig");
        string? receivedPath = null;
        var provider = new KubernetesConfigProvider(CreateOptions(kubeConfig: kubeConfig));

        var config = provider.Create(
            path =>
            {
                receivedPath = path;

                return new KubernetesClientConfiguration { Host = "https://kubeconfig.example.com" };
            },
            () => throw new InvalidOperationException("In-cluster config should not be used."),
            () => throw new InvalidOperationException("Default config should not be used."));

        Assert.Equal("https://kubeconfig.example.com", config.Host);
        Assert.Equal(kubeConfig, receivedPath);
        Assert.Equal(K8sConventions.ServiceName, config.UserAgent);
    }

    [Fact]
    public void Create_UsesInClusterFactory_WhenEnabled()
    {
        var provider = new KubernetesConfigProvider(CreateOptions(isInClusterConfigEnabled: true));

        var config = provider.Create(
            _ => throw new InvalidOperationException("Kubeconfig should not be used."),
            () => new KubernetesClientConfiguration { Host = "https://in-cluster.example.com" },
            () => throw new InvalidOperationException("Default config should not be used."));

        Assert.Equal("https://in-cluster.example.com", config.Host);
        Assert.Equal(K8sConventions.ServiceName, config.UserAgent);
    }

    [Fact]
    public void Create_UsesDefaultFactory_WhenDevelopmentFallbackAllowed()
    {
        var provider = new KubernetesConfigProvider(CreateOptions());

        var config = provider.Create(
            _ => throw new InvalidOperationException("Kubeconfig should not be used."),
            () => throw new InvalidOperationException("In-cluster config should not be used."),
            () => new KubernetesClientConfiguration { Host = "http://localhost:8080" });

        Assert.Equal("http://localhost:8080", config.Host);
        Assert.Equal(K8sConventions.ServiceName, config.UserAgent);
    }

    [Fact]
    public void Create_WithBothAuthModes_Throws()
    {
        var provider = new KubernetesConfigProvider(CreateOptions(
            kubeConfig: ProductionPath("kubeconfig"),
            isInClusterConfigEnabled: true));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.Create(
            _ => throw new InvalidOperationException("Kubeconfig should not be used."),
            () => throw new InvalidOperationException("In-cluster config should not be used."),
            () => throw new InvalidOperationException("Default config should not be used.")));

        Assert.Contains(K8sConventions.EnvironmentVariables.KubeConfig, exception.Message);
        Assert.Contains(K8sConventions.EnvironmentVariables.UseInClusterConfig, exception.Message);
    }

    [Fact]
    public void Create_WithProductionModeAndNoAuth_Throws()
    {
        var provider = new KubernetesConfigProvider(CreateOptions(runtimeMode: RuntimeMode.Production));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.Create(
            _ => throw new InvalidOperationException("Kubeconfig should not be used."),
            () => throw new InvalidOperationException("In-cluster config should not be used."),
            () => throw new InvalidOperationException("Default config should not be used.")));

        Assert.Contains(K8sConventions.EnvironmentVariables.KubeConfig, exception.Message);
        Assert.Contains(K8sConventions.EnvironmentVariables.UseInClusterConfig, exception.Message);
    }

    private static K8sMcpOptions CreateOptions(
        RuntimeMode runtimeMode = RuntimeMode.Development,
        string? kubeConfig = null,
        bool isInClusterConfigEnabled = false) =>
        new(
            new HashSet<string>(["mcp-nginx-demo"], StringComparer.Ordinal),
            ProductionPath("approvals"),
            runtimeMode,
            IsApprovalRootExplicit: true,
            HasExplicitAllowedNamespaces: true,
            kubeConfig,
            isInClusterConfigEnabled);

    private static string ProductionPath(string fileName)
    {
        string root = Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? Path.DirectorySeparatorChar.ToString();

        return Path.Combine(root, "var", "lib", "infra-gate-tests", fileName);
    }
}
