using InfraGate.KubernetesAdapter;
using InfraGate.RuntimeSafety;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesConventionsTests
{
    [Fact]
    public void RegisterInfraGateEnvVarMappings_NullMappings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            KubernetesConventions.RegisterInfraGateEnvVarMappings(null!));
    }

    [Theory]
    [InlineData("KUBECONFIG", "InfraGate:Kubernetes:KubeConfig")]
    [InlineData("K8S_MCP_USE_IN_CLUSTER", "InfraGate:Kubernetes:UseInClusterConfig")]
    [InlineData("K8S_MCP_ALLOWED_NAMESPACES", "InfraGate:Kubernetes:AllowedNamespaces")]
    [InlineData("K8S_MCP_LOG_PATH", "InfraGate:Kubernetes:LogPath")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_REQUIRED", "InfraGate:DownstreamAuth:Required")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_AUTHORITY", "InfraGate:DownstreamAuth:Authority")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_METADATA_ADDRESS", "InfraGate:DownstreamAuth:MetadataAddress")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_REQUIRE_HTTPS_METADATA", "InfraGate:DownstreamAuth:RequireHttpsMetadata")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_AUDIENCE", "InfraGate:DownstreamAuth:Audience")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_SCOPE", "InfraGate:DownstreamAuth:Scope")]
    public void RegisterInfraGateEnvVarMappings_RegistersExpectedMapping(string envVar, string configKey)
    {
        var mappings = new InfraGateEnvVarMappings();

        KubernetesConventions.RegisterInfraGateEnvVarMappings(mappings);

        Assert.Equal(configKey, mappings.GetConfigurationKey(envVar));
    }

    [Fact]
    public void DeploymentRef_ReturnsObjectRefWithAppsV1AndDeploymentKind()
    {
        var result = KubernetesConventions.KubernetesResources.DeploymentRef("production", "web-api");

        Assert.Equal("apps/v1", result.ApiVersion);
        Assert.Equal("Deployment", result.Kind);
        Assert.Equal("production", result.Namespace);
        Assert.Equal("web-api", result.Name);
    }

    [Fact]
    public void IsDeployment_AppsV1DeploymentRef_ReturnsTrue()
    {
        var obj = new KubernetesObjectRef("apps/v1", "Deployment", "production", "web-api");

        Assert.True(KubernetesConventions.KubernetesResources.IsDeployment(obj));
    }

    [Fact]
    public void IsDeployment_WrongApiVersion_ReturnsFalse()
    {
        var obj = new KubernetesObjectRef("v1", "Deployment", "production", "web-api");

        Assert.False(KubernetesConventions.KubernetesResources.IsDeployment(obj));
    }

    [Fact]
    public void IsDeployment_WrongKind_ReturnsFalse()
    {
        var obj = new KubernetesObjectRef("apps/v1", "ReplicaSet", "production", "web-api");

        Assert.False(KubernetesConventions.KubernetesResources.IsDeployment(obj));
    }
}
