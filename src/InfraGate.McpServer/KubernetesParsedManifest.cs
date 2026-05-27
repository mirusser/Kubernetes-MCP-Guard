using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.PlanBuilding;
using k8s;
using k8s.Models;

namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record class KubernetesParsedManifest(
    IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Objects,
    KubernetesObjectRef[] ObjectRefs);
