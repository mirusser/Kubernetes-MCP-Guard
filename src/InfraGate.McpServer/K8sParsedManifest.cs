using InfraGate.Approvals;
using k8s;
using k8s.Models;

namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record K8sParsedManifest(
    IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Objects,
    K8sObjectRef[] ObjectRefs);
