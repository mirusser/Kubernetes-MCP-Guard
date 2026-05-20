using InfraGate.McpServer;
using k8s.Models;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesManifestParserTests
{
    [Fact]
    public void ParseSupported_DefaultsNamespace_WhenManifestOmitsIt()
    {
        var parsed = KubernetesManifestParser.ParseSupported(ValidManifest, "demo");

        Assert.Equal(3, parsed.ObjectRefs.Length);
        Assert.All(parsed.ObjectRefs, obj => Assert.Equal("demo", obj.Namespace));
        Assert.Contains(parsed.Objects.OfType<V1Deployment>(), deployment => deployment.Metadata.NamespaceProperty == "demo");
    }

    [Fact]
    public void ParseSupported_RejectsUnsupportedKind()
    {
        var manifest = """
                       apiVersion: v1
                       kind: Secret
                       metadata:
                         name: demo-secret
                       """;

        var ex = Assert.Throws<KubernetesValidationException>(() =>
            KubernetesManifestParser.ParseSupported(manifest, "demo"));

        Assert.Contains("Unsupported Kubernetes kind", ex.Message);
    }

    [Fact]
    public void ParseSupported_RejectsMissingName()
    {
        var manifest = """
                       apiVersion: v1
                       kind: ConfigMap
                       metadata:
                         namespace: demo
                       """;

        var ex = Assert.Throws<KubernetesValidationException>(() =>
            KubernetesManifestParser.ParseSupported(manifest, "demo"));

        Assert.Contains("metadata.name", ex.Message);
    }

    [Fact]
    public void ParseSupported_RejectsMismatchedNamespace()
    {
        var manifest = """
                       apiVersion: v1
                       kind: ConfigMap
                       metadata:
                         name: demo-config
                         namespace: other
                       """;

        var ex = Assert.Throws<KubernetesValidationException>(() =>
            KubernetesManifestParser.ParseSupported(manifest, "demo"));

        Assert.Contains("tool namespace is 'demo'", ex.Message);
    }

    private const string ValidManifest = """
                                         apiVersion: apps/v1
                                         kind: Deployment
                                         metadata:
                                           name: demo
                                         spec:
                                           replicas: 1
                                           selector:
                                             matchLabels:
                                               app: demo
                                           template:
                                             metadata:
                                               labels:
                                                 app: demo
                                             spec:
                                               containers:
                                                 - name: nginx
                                                   image: nginx:1.27-alpine
                                         ---
                                         apiVersion: v1
                                         kind: Service
                                         metadata:
                                           name: demo
                                         spec:
                                           selector:
                                             app: demo
                                           ports:
                                             - port: 80
                                               targetPort: 80
                                         ---
                                         apiVersion: v1
                                         kind: ConfigMap
                                         metadata:
                                           name: demo-config
                                         data:
                                           hello: world
                                         """;
}
