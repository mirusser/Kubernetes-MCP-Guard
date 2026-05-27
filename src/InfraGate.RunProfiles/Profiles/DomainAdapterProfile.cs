namespace InfraGate.RunProfiles;

internal sealed record class DomainAdapterProfile(
    string Name,
    string Type,
    KubernetesAdapterProfile? Kubernetes);
