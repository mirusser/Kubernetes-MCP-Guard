using InfraGate.McpServer.Models;
using k8s;
using k8s.Models;

namespace InfraGate.McpServer;

public sealed record class KubernetesParsedManifest(
    IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Objects,
    KubernetesObjectRef[] ObjectRefs);
