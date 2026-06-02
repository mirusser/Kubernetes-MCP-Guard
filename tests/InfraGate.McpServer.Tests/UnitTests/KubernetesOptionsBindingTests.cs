using Microsoft.Extensions.Configuration;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesOptionsBindingTests
{
    [Fact]
    public void Bind_FullSection_PopulatesAllFields()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Kubernetes:KubeConfig"] = "/run/kube/demo.config",
                ["InfraGate:Kubernetes:UseInClusterConfig"] = "false",
                ["InfraGate:Kubernetes:AllowedNamespaces:0"] = "ns-a",
                ["InfraGate:Kubernetes:AllowedNamespaces:1"] = "ns-b",
                ["InfraGate:Kubernetes:LogPath"] = "/tmp/mcp-server.log"
            })
            .Build();

        var options = configuration
            .GetSection(KubernetesOptions.SectionName)
            .Get<KubernetesOptions>();

        Assert.NotNull(options);
        Assert.Equal("/run/kube/demo.config", options!.KubeConfig);
        Assert.False(options.UseInClusterConfig);
        Assert.Equal(["ns-a", "ns-b"], options.AllowedNamespaces);
        Assert.Equal("/tmp/mcp-server.log", options.LogPath);
    }

    [Fact]
    public void Bind_SectionWithOnlyKubeConfig_KeepsDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Kubernetes:KubeConfig"] = "/run/kube/demo.config"
            })
            .Build();

        var options = configuration
            .GetSection(KubernetesOptions.SectionName)
            .Get<KubernetesOptions>();

        Assert.NotNull(options);
        Assert.Equal("/run/kube/demo.config", options!.KubeConfig);
        Assert.False(options.UseInClusterConfig);
        Assert.Empty(options.AllowedNamespaces);
        Assert.Null(options.LogPath);
    }

    [Fact]
    public void Bind_AbsentSection_ReturnsNull()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        var options = configuration
            .GetSection(KubernetesOptions.SectionName)
            .Get<KubernetesOptions>();

        Assert.Null(options);
    }
}
