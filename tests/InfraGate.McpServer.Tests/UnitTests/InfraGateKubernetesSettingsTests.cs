using InfraGate.McpServer;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class InfraGateKubernetesSettingsTests
{
    [Fact]
    public void BindFromConfiguration_PopulatesAllFields()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Kubernetes:KubeConfig"] = "/kube/config",
                ["InfraGate:Kubernetes:UseInClusterConfig"] = "false",
                ["InfraGate:Kubernetes:AllowedNamespaces:0"] = "ns1",
                ["InfraGate:Kubernetes:AllowedNamespaces:1"] = "ns2",
                ["InfraGate:Kubernetes:LogPath"] = "/tmp/mcp-server.log"
            })
            .Build();

        var settings = configuration
            .GetSection("InfraGate:Kubernetes")
            .Get<InfraGateKubernetesSettings>();

        Assert.NotNull(settings);
        Assert.Equal("/kube/config", settings!.KubeConfig);
        Assert.False(settings.UseInClusterConfig);
        Assert.NotNull(settings.AllowedNamespaces);
        Assert.Equal(["ns1", "ns2"], settings.AllowedNamespaces);
        Assert.Equal("/tmp/mcp-server.log", settings.LogPath);
    }

    [Fact]
    public void BindFromConfiguration_EmptyNamespaceArray_IsNull()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Kubernetes:KubeConfig"] = "/kube/config"
            })
            .Build();

        var settings = configuration
            .GetSection("InfraGate:Kubernetes")
            .Get<InfraGateKubernetesSettings>();

        Assert.NotNull(settings);
        Assert.Null(settings!.AllowedNamespaces);
    }
}
