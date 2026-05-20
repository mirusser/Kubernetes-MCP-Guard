using System.Text.Json;
using System.Text.Json.Nodes;
using InfraGate.McpServer.Diff;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesObjectNormalizerTests
{
    [Fact]
    public void NormalizeJson_RemovesStatusField()
    {
        string json = """
            {
              "apiVersion": "apps/v1",
              "kind": "Deployment",
              "metadata": { "name": "demo" },
              "status": { "readyReplicas": 1, "availableReplicas": 1 }
            }
            """;

        var result = KubernetesObjectNormalizer.NormalizeJson(json);

        using var document = JsonDocument.Parse(result);
        Assert.False(document.RootElement.TryGetProperty("status", out _));
        Assert.Equal("Deployment", document.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void NormalizeJson_RemovesManagedFields()
    {
        string json = """
            {
              "metadata": {
                "name": "demo",
                "managedFields": [{ "manager": "kubectl", "operation": "Apply" }]
              }
            }
            """;

        var result = KubernetesObjectNormalizer.NormalizeJson(json);

        using var document = JsonDocument.Parse(result);
        var metadata = document.RootElement.GetProperty("metadata");
        Assert.False(metadata.TryGetProperty("managedFields", out _));
        Assert.Equal("demo", metadata.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("resourceVersion", "12345")]
    [InlineData("uid", "abc-123")]
    [InlineData("creationTimestamp", "2024-01-01T00:00:00Z")]
    [InlineData("generation", "3")]
    public void NormalizeJson_RemovesServerGeneratedMetadataFields(string field, string value)
    {
        string json = $$"""
            {
              "metadata": {
                "name": "demo",
                "{{field}}": "{{value}}"
              }
            }
            """;

        var result = KubernetesObjectNormalizer.NormalizeJson(json);

        using var document = JsonDocument.Parse(result);
        var metadata = document.RootElement.GetProperty("metadata");
        Assert.False(metadata.TryGetProperty(field, out _));
    }

    [Fact]
    public void NormalizeJson_RemovesLastAppliedConfigurationAnnotation()
    {
        string json = """
            {
              "metadata": {
                "name": "demo",
                "annotations": {
                  "kubectl.kubernetes.io/last-applied-configuration": "{\"spec\":{}}"
                }
              }
            }
            """;

        var result = KubernetesObjectNormalizer.NormalizeJson(json);

        using var document = JsonDocument.Parse(result);
        var metadata = document.RootElement.GetProperty("metadata");
        Assert.False(metadata.TryGetProperty("annotations", out _));
    }

    [Fact]
    public void NormalizeJson_RemovesAnnotationsObjectWhenItBecomesEmpty()
    {
        string json = """
            {
              "metadata": {
                "name": "demo",
                "annotations": {
                  "kubectl.kubernetes.io/last-applied-configuration": "{}"
                }
              }
            }
            """;

        var result = KubernetesObjectNormalizer.NormalizeJson(json);

        using var document = JsonDocument.Parse(result);
        var metadata = document.RootElement.GetProperty("metadata");
        Assert.False(metadata.TryGetProperty("annotations", out _));
    }

    [Fact]
    public void NormalizeJson_PreservesNonNoisyAnnotations()
    {
        string json = """
            {
              "metadata": {
                "name": "demo",
                "annotations": {
                  "kubectl.kubernetes.io/last-applied-configuration": "{}",
                  "custom.io/owner": "team-a"
                }
              }
            }
            """;

        var result = KubernetesObjectNormalizer.NormalizeJson(json);

        using var document = JsonDocument.Parse(result);
        var annotations = document.RootElement.GetProperty("metadata").GetProperty("annotations");
        Assert.False(annotations.TryGetProperty("kubectl.kubernetes.io/last-applied-configuration", out _));
        Assert.Equal("team-a", annotations.GetProperty("custom.io/owner").GetString());
    }

    [Fact]
    public void NormalizeJson_SortsTopLevelKeysAlphabetically()
    {
        string json = """
            {
              "spec": { "replicas": 1 },
              "metadata": { "name": "demo" },
              "kind": "Deployment",
              "apiVersion": "apps/v1"
            }
            """;

        var result = KubernetesObjectNormalizer.NormalizeJson(json);

        using var document = JsonDocument.Parse(result);
        var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), keys);
    }

    [Fact]
    public void NormalizeJson_SortsNestedObjectKeysAlphabetically()
    {
        string json = """
            {
              "spec": {
                "template": { "spec": {} },
                "selector": { "matchLabels": { "app": "demo" } },
                "replicas": 1
              }
            }
            """;

        var result = KubernetesObjectNormalizer.NormalizeJson(json);

        using var document = JsonDocument.Parse(result);
        var specKeys = document.RootElement.GetProperty("spec").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(specKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), specKeys);
    }

    [Fact]
    public void NormalizeJson_HandlesEmptyInput()
    {
        var result = KubernetesObjectNormalizer.NormalizeJson("{}");

        using var document = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateObject());
    }

    [Fact]
    public void NormalizeJson_PreservesArrayOrder()
    {
        string json = """
            {
              "spec": {
                "containers": [
                  { "name": "nginx", "image": "nginx:latest" },
                  { "name": "sidecar", "image": "busybox:latest" }
                ]
              }
            }
            """;

        var result = KubernetesObjectNormalizer.NormalizeJson(json);

        using var document = JsonDocument.Parse(result);
        var containers = document.RootElement.GetProperty("spec").GetProperty("containers");
        Assert.Equal("nginx", containers[0].GetProperty("name").GetString());
        Assert.Equal("sidecar", containers[1].GetProperty("name").GetString());
    }

    [Fact]
    public void ToYaml_ProducesKeyValuePairsFromNormalizedJson()
    {
        string json = """
            {
              "apiVersion": "apps/v1",
              "kind": "Deployment",
              "metadata": { "name": "demo" }
            }
            """;

        var normalized = KubernetesObjectNormalizer.NormalizeJson(json);
        var yaml = KubernetesObjectNormalizer.ToYaml(normalized);

        Assert.Contains("apiVersion: apps/v1", yaml);
        Assert.Contains("kind: Deployment", yaml);
        Assert.Contains("name: demo", yaml);
    }
}
