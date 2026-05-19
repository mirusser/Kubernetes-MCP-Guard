namespace InfraGate.KubernetesAdapter.Policy;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record KubernetesPolicyOptions
{
    public bool DenyPrivilegedContainers { get; init; } = true;
    public bool DenyHostPathVolumes { get; init; } = true;
    public bool DenyHostNetwork { get; init; } = true;
    public bool DenyHostPid { get; init; } = true;
    public bool DenyHostIpc { get; init; } = true;
    public bool DenyAddedCapabilities { get; init; } = true;
    public bool DenyLatestImageTag { get; init; } = true;
    public bool DenyServiceTypeNodePort { get; init; } = true;
    public bool DenyServiceTypeLoadBalancer { get; init; } = true;
    public bool WarnOnConfigMapSecretLikeKeys { get; init; } = true;

    public static KubernetesPolicyOptions Default { get; } = new();
}
