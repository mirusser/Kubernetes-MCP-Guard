namespace InfraGate.KubernetesAdapter.Policy;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public enum K8sPolicySeverity { Warning, Deny }
