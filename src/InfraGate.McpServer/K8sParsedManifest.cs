using InfraGate.Approvals;
using k8s;
using k8s.Models;

namespace InfraGate.McpServer;

public sealed record K8sParsedManifest(
    IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Objects,
    K8sObjectRef[] ObjectRefs);
