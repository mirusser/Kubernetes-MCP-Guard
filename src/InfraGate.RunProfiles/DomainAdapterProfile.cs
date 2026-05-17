namespace InfraGate.RunProfiles;

internal sealed record DomainAdapterProfile(
    string Name,
    string Type,
    KubernetesAdapterProfile? Kubernetes);
