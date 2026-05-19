using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using InfraGate.McpServer;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sManagerConfigTests
{
    [Fact]
    public async Task GetAllowedNamespacesAsync_ReturnsSingleNamespace()
    {
        var manager = CreateManager("demo");

        var result = await manager.GetAllowedNamespacesAsync();

        var doc = JsonDocument.Parse(result);
        var namespaces = doc.RootElement.GetProperty("allowedNamespaces").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();

        Assert.Equal(["demo"], namespaces);
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task GetAllowedNamespacesAsync_ReturnsAllNamespacesAlphabeticallySorted()
    {
        var manager = CreateManager("zeta", "alpha", "beta");

        var result = await manager.GetAllowedNamespacesAsync();

        var doc = JsonDocument.Parse(result);
        var namespaces = doc.RootElement.GetProperty("allowedNamespaces").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();

        Assert.Equal(["alpha", "beta", "zeta"], namespaces);
        Assert.Equal(3, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task GetAllowedNamespacesAsync_ReturnsValidJson()
    {
        var manager = CreateManager("demo");

        var result = await manager.GetAllowedNamespacesAsync();

        Assert.True(IsValidJson(result), $"Result was not valid JSON: {result}");
    }

    [Fact]
    public async Task GetAllowedNamespacesAsync_DoesNotRequireKubernetesClient()
    {
        // client is null — this tool must not touch the K8s API
        var manager = CreateManager("demo");

        var ex = await Record.ExceptionAsync(() => manager.GetAllowedNamespacesAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task GetAllowedNamespacesAsync_ReturnsEmptyArrayWithZeroCount_WhenNoNamespacesConfigured()
    {
        var manager = CreateManager();

        var result = await manager.GetAllowedNamespacesAsync();

        var doc = JsonDocument.Parse(result);
        var namespaces = doc.RootElement.GetProperty("allowedNamespaces").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();

        Assert.Empty(namespaces);
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
    }

    private static K8sManager CreateManager(params string[] namespaces)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new K8SMcpOptions(
            new HashSet<string>(namespaces, StringComparer.Ordinal),
            root);

        return new K8sManager(options, client: null!, NullLogger<K8sManager>.Instance);
    }

    private static bool IsValidJson(string value)
    {
        try
        {
            JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
