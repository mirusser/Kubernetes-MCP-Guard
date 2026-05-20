namespace InfraGate.KubernetesAdapter;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record class KubernetesObjectRef(
    string ApiVersion,
    string Kind,
    string Namespace,
    string Name);
