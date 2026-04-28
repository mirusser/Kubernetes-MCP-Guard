using InfraGate.McpServer;

namespace InfraGate.McpServer.Tests;

public sealed class K8sMcpOptionsTests
{
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
}
