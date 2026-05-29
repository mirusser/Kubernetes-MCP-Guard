namespace InfraGate.KubernetesAdapter.Policy;

public sealed record class KubernetesPolicyFinding(
    KubernetesPolicySeverity Severity,
    string Code,
    string ObjectRef,
    string Message);
